#define _POSIX_C_SOURCE 200809L
#include "vessel.h"

#include <errno.h>
#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
typedef HANDLE thread_t;
typedef HANDLE gate_t;
#define THREAD_ENTRY DWORD WINAPI
#else
#include <pthread.h>
#include <sys/resource.h>
#include <time.h>
typedef pthread_t thread_t;
typedef struct { pthread_mutex_t lock; pthread_cond_t signal; int open; } gate_t;
#define THREAD_ENTRY void*
#endif

enum operation { READ, PUT, PULL, OVERWRITE, DEDUP, DELETE_OBJECT, REOPEN };
static const char* const operations[] = { "read", "put", "pull", "overwrite", "dedup", "delete", "reopen" };
static const char* const codecs[] = { "none", "zstd", "lz4" };
static const vessel_object_info_t no_info = { 0 };

struct memory {
    uint64_t rss, peak_rss, private_bytes;
};

struct run {
    enum operation operation;
    int32_t codec, size;
    size_t iterations;
    vessel_t store;
    gate_t gate;
};

struct worker {
    struct run* run;
    size_t index;
    uint8_t* payload;
    double* latency;
    vessel_error_t error;
};

static _Noreturn void fail(const char* message)
{
    fprintf(stderr, "%s\n", message);
    exit(EXIT_FAILURE);
}

static void check(vessel_error_t status, const char* operation)
{
    if (status == VESSEL_SUCCESS) return;
    fprintf(stderr, "%s returned Vessel status %" PRId32 "\n", operation, status);
    exit(EXIT_FAILURE);
}

static double now(void)
{
#ifdef _WIN32
    static double scale;
    LARGE_INTEGER ticks;
    if (scale == 0) {
        QueryPerformanceFrequency(&ticks);
        scale = 1.0 / (double)ticks.QuadPart;
    }
    QueryPerformanceCounter(&ticks);
    return (double)ticks.QuadPart * scale;
#else
    struct timespec ticks;
    if (clock_gettime(CLOCK_MONOTONIC, &ticks)) fail("Cannot read the monotonic clock");
    return (double)ticks.tv_sec + (double)ticks.tv_nsec / 1e9;
#endif
}

static struct memory memory_usage(void)
{
    struct memory result = { 0 };
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS_EX values = { 0 };
    values.cb = (DWORD)sizeof(values);
    if (GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS*)&values, values.cb)) {
        result.rss = values.WorkingSetSize;
        result.peak_rss = values.PeakWorkingSetSize;
        result.private_bytes = values.PrivateUsage;
    }
#else
    struct rusage values;
    if (!getrusage(RUSAGE_SELF, &values)) {
        result.peak_rss = (uint64_t)values.ru_maxrss;
#ifndef __APPLE__
        result.peak_rss *= 1024;
#endif
    }
#endif
    return result;
}

static void gate_init(gate_t* gate)
{
#ifdef _WIN32
    *gate = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!*gate) fail("Cannot create a worker gate");
#else
    gate->open = 0;
    if (pthread_mutex_init(&gate->lock, NULL) || pthread_cond_init(&gate->signal, NULL))
        fail("Cannot create a worker gate");
#endif
}

static void gate_wait(gate_t* gate)
{
#ifdef _WIN32
    WaitForSingleObject(*gate, INFINITE);
#else
    pthread_mutex_lock(&gate->lock);
    while (!gate->open) pthread_cond_wait(&gate->signal, &gate->lock);
    pthread_mutex_unlock(&gate->lock);
#endif
}

static void gate_open(gate_t* gate)
{
#ifdef _WIN32
    SetEvent(*gate);
#else
    pthread_mutex_lock(&gate->lock);
    gate->open = 1;
    pthread_cond_broadcast(&gate->signal);
    pthread_mutex_unlock(&gate->lock);
#endif
}

static void gate_close(gate_t* gate)
{
#ifdef _WIN32
    CloseHandle(*gate);
#else
    pthread_cond_destroy(&gate->signal);
    pthread_mutex_destroy(&gate->lock);
#endif
}

static void key_for(char key[64], size_t worker, size_t iteration)
{
    snprintf(key, 64, "bench/%zu/%zu", worker, iteration);
}

static void make_payload(uint8_t* payload, int32_t length)
{
    uint32_t random = 0xab373262;
    for (int32_t i = 0; i < length / 2; ++i) {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        payload[i] = (uint8_t)random;
    }
    memset(payload + length / 2, 'v', (size_t)(length - length / 2));
}

static vessel_error_t read_object(vessel_t store, const char* key, int32_t size)
{
    vessel_stream_t reader = NULL;
    vessel_error_t status = vessel_stream_open(store, key, &reader);
    if (status != VESSEL_SUCCESS) return status;
    uint8_t buffer[64 * 1024];
    int64_t total = 0;
    for (;;) {
        int32_t count;
        status = vessel_stream_read(reader, buffer, (int32_t)sizeof(buffer), &count);
        if (status != VESSEL_SUCCESS || !count) break;
        total += count;
    }
    vessel_stream_close(reader);
    return status == VESSEL_SUCCESS && total != size ? VESSEL_ERR_IO : status;
}

struct input { const uint8_t* data; int32_t remaining; };

static int32_t VESSEL_CALL read_input(void* context, uint8_t* output, int32_t capacity)
{
    struct input* input = context;
    int32_t count = capacity < input->remaining ? capacity : input->remaining;
    memcpy(output, input->data, (size_t)count);
    input->data += count;
    input->remaining -= count;
    return count;
}

static THREAD_ENTRY run_worker(void* state)
{
    struct worker* worker = state;
    struct run* run = worker->run;
    gate_wait(&run->gate);
    for (size_t i = 0; i < run->iterations; ++i) {
        char key[64];
        size_t key_index = run->operation == READ || run->operation == OVERWRITE ? 0 : i;
        key_for(key, worker->index, key_index);
        if (run->operation == PUT || run->operation == PULL || run->operation == OVERWRITE) {
            uint64_t marker = UINT64_C(1) + (uint64_t)worker->index * run->iterations + i;
            memcpy(worker->payload, &marker, sizeof(marker));
        }

        double start = now();
        switch (run->operation) {
            case READ: worker->error = read_object(run->store, key, run->size); break;
            case DELETE_OBJECT: {
                int32_t deleted;
                worker->error = vessel_delete(run->store, key, &deleted);
                if (worker->error == VESSEL_SUCCESS && !deleted) worker->error = VESSEL_ERR_NOT_FOUND;
                break;
            }
            case PULL: {
                struct input input = { worker->payload, run->size };
                worker->error = vessel_put_stream(run->store, key, read_input, &input, 0, no_info,
                    run->codec, VESSEL_LEVEL_BALANCED, NULL);
                break;
            }
            default:
                worker->error = vessel_put_ex(run->store, key, worker->payload, run->size,
                    run->operation == OVERWRITE, no_info, run->codec, VESSEL_LEVEL_BALANCED, NULL);
                break;
        }
        worker->latency[i] = now() - start;
        if (worker->error != VESSEL_SUCCESS) break;
    }
    return 0;
}

static void start_worker(thread_t* thread, struct worker* worker)
{
#ifdef _WIN32
    *thread = CreateThread(NULL, 0, run_worker, worker, 0, NULL);
    if (!*thread) fail("Cannot start a worker");
#else
    if (pthread_create(thread, NULL, run_worker, worker)) fail("Cannot start a worker");
#endif
}

static void join_worker(thread_t thread)
{
#ifdef _WIN32
    WaitForSingleObject(thread, INFINITE);
    CloseHandle(thread);
#else
    pthread_join(thread, NULL);
#endif
}

static size_t number(const char* value, size_t min, size_t max)
{
    char* end;
    errno = 0;
    uintmax_t parsed = strtoumax(value, &end, 10);
    if (errno || *end || *value < '0' || *value > '9' || parsed < min || parsed > max)
        fail("Numeric benchmark argument is outside its supported range");
    return (size_t)parsed;
}

static size_t choice(const char* value, const char* const* names, size_t count)
{
    for (size_t i = 0; i < count; ++i) if (!strcmp(value, names[i])) return i;
    fail("Unknown benchmark operation or codec");
}

static int compare_latency(const void* left, const void* right)
{
    double a = *(const double*)left;
    double b = *(const double*)right;
    return (a > b) - (a < b);
}

static double percentile(const double* sorted, size_t count, size_t percent)
{
    return sorted[(count * percent + 99) / 100 - 1] * 1000;
}

static void seed(struct run* run, uint8_t* payload, size_t workers, int reuse)
{
    if (run->operation == DEDUP)
        check(vessel_put_ex(run->store, "seed", payload, run->size, 0, no_info,
            run->codec, VESSEL_LEVEL_BALANCED, NULL), "seed deduplicated payload");
    if (run->operation != READ && run->operation != OVERWRITE && run->operation != DELETE_OBJECT) return;
    for (size_t worker = 0; worker < workers; ++worker) {
        // Distinct read targets. An overwrite starts from marker zero and uses distinct replacement payloads.
        uint64_t marker = run->operation == READ ? (uint64_t)worker : 0;
        if (!reuse) memcpy(payload, &marker, sizeof(marker));
        size_t keys = run->operation == DELETE_OBJECT ? run->iterations : 1;
        for (size_t i = 0; i < keys; ++i) {
            char key[64];
            key_for(key, worker, i);
            if (reuse) {
                vessel_metadata_result_t result = { 0 };
                check(vessel_get_metadata_ex(run->store, key, &result), "check existing fixture");
                if (result.metadata.compression_type != run->codec || result.metadata.original_size != run->size)
                    fail("Existing read fixture does not match the requested codec and size");
                vessel_free_metadata(&result.metadata);
            } else {
                check(vessel_put_ex(run->store, key, payload, run->size, 0, no_info,
                    run->codec, VESSEL_LEVEL_BALANCED, NULL), "seed object");
            }
            if (run->operation == READ) check(read_object(run->store, key, run->size), "warm read");
        }
    }
}

int main(int argc, char** argv)
{
    if (argc != 7 && (argc != 8 || strcmp(argv[7], "--reuse")))
        fail("Usage: vessel-benchmark <store> <read|put|pull|overwrite|dedup|delete|reopen> <none|zstd|lz4> <bytes> <workers> <operations_per_worker> [--reuse]");
    struct run run = { 0 };
    const char* path = argv[1];
    run.operation = (enum operation)choice(argv[2], operations, sizeof(operations) / sizeof(*operations));
    int reuse = argc == 8;
    if (reuse && run.operation != READ) fail("--reuse requires the read operation");
    run.codec = (int32_t)choice(argv[3], codecs, sizeof(codecs) / sizeof(*codecs));
    run.size = (int32_t)number(argv[4], run.operation == REOPEN ? 0 : sizeof(uint64_t), INT32_MAX);
    size_t count = number(argv[5], 1, run.operation == REOPEN ? 1 : 256);
    run.iterations = number(argv[6], 1, 1000000);
    size_t total = count * run.iterations;
    if (total > SIZE_MAX / 100) fail("Too many operations");
    double* latency = malloc(total * sizeof(*latency));
    if (!latency) fail("Cannot allocate latency samples");
    struct memory before, after = { 0 };
    double first_open = 0;
    size_t harness_payload_bytes = 0;
    double start, elapsed;

    if (run.operation == REOPEN) {
        before = memory_usage();
        elapsed = 0;
        for (size_t i = 0; i < total; ++i) {
            start = now();
            check(vessel_init(path, &run.store), "reopen store");
            latency[i] = now() - start;
            elapsed += latency[i];
            if (i == total - 1) after = memory_usage();
            vessel_dispose(run.store);
        }
        first_open = latency[0];
    } else {
        start = now();
        check(vessel_init(path, &run.store), "open store");
        first_open = now() - start;
        uint8_t* common = reuse ? NULL : malloc((size_t)run.size);
        struct worker* workers = calloc(count, sizeof(*workers));
        thread_t* threads = malloc(count * sizeof(*threads));
        if ((!reuse && !common) || !workers || !threads) fail("Cannot allocate worker fixtures");
        if (!reuse) make_payload(common, run.size);
        seed(&run, common, count, reuse);
        int private_payload = run.operation == PUT || run.operation == PULL || run.operation == OVERWRITE;
        gate_init(&run.gate);
        for (size_t i = 0; i < count; ++i) {
            struct worker* worker = &workers[i];
            worker->run = &run;
            worker->index = i;
            worker->latency = latency + i * run.iterations;
            if (run.operation == DEDUP) worker->payload = common;
            if (private_payload) {
                worker->payload = malloc((size_t)run.size);
                if (!worker->payload) fail("Cannot allocate worker payload");
                memcpy(worker->payload, common, (size_t)run.size);
                harness_payload_bytes += (size_t)run.size;
            }
            start_worker(&threads[i], worker);
        }
        if (run.operation == DEDUP) harness_payload_bytes = (size_t)run.size;
        else free(common);
        before = memory_usage();
        start = now();
        gate_open(&run.gate);
        for (size_t i = 0; i < count; ++i) {
            join_worker(threads[i]);
            check(workers[i].error, operations[run.operation]);
        }
        elapsed = now() - start;
        after = memory_usage();
        if (private_payload) for (size_t i = 0; i < count; ++i) free(workers[i].payload);
        if (run.operation == DEDUP) free(common);
        free(workers);
        free(threads);
        gate_close(&run.gate);
        vessel_dispose(run.store);
    }

    qsort(latency, total, sizeof(*latency), compare_latency);
    double payload_mib = (run.operation == REOPEN || run.operation == DELETE_OBJECT) ? 0 :
        (double)run.size * (double)total / (1024 * 1024);
    puts("operation,codec,payload_bytes,workers,operations,first_open_ms,elapsed_ms,ops_s,mib_s,p50_ms,p95_ms,p99_ms,harness_payload_bytes,rss_before_bytes,rss_after_bytes,peak_rss_bytes,private_before_bytes,private_after_bytes");
    printf("%s,%s,%" PRId32 ",%zu,%zu,%.3f,%.3f,%.1f,%.1f,%.3f,%.3f,%.3f,%zu,%" PRIu64 ",%" PRIu64 ",%" PRIu64 ",%" PRIu64 ",%" PRIu64 "\n",
        operations[run.operation], codecs[run.codec], run.size, count, total, first_open * 1000,
        elapsed * 1000, (double)total / elapsed, payload_mib / elapsed,
        percentile(latency, total, 50), percentile(latency, total, 95), percentile(latency, total, 99),
        harness_payload_bytes, before.rss, after.rss, after.peak_rss, before.private_bytes, after.private_bytes);
    free(latency);
    return EXIT_SUCCESS;
}
