#include "vessel.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

#if UINTPTR_MAX == UINT64_MAX
_Static_assert(sizeof(vessel_object_t) == 48, "unexpected object layout");
_Static_assert(sizeof(vessel_metadata_t) == 80, "unexpected metadata layout");
_Static_assert(sizeof(vessel_metadata_result_t) == 88, "unexpected result layout");
_Static_assert(offsetof(vessel_metadata_t, creation_date_unix) == 24, "unexpected timestamp layout");
_Static_assert(offsetof(vessel_options_t, log) == 8, "unexpected log callback layout");
#endif

struct source {
    size_t remaining;
    size_t offset;
    size_t cancel_at;
};

static int32_t VESSEL_CALL supply(void* context, uint8_t* output, int32_t capacity)
{
    struct source* source = context;
    if (source->cancel_at && source->offset >= source->cancel_at) return VESSEL_ERR_CANCELLED;
    size_t count = source->remaining < (size_t)capacity ? source->remaining : (size_t)capacity;

    for (size_t index = 0; index < count; ++index)
        output[index] = (uint8_t)((source->offset + index) % 251);

    source->offset += count;
    source->remaining -= count;
    return (int32_t)count;
}

static int32_t VESSEL_CALL first_key(void* context, const char* key)
{
    int* count = context;
    assert(strcmp(key, "smoke/\xc3\xa9" "tude") == 0);
    (*count)++;
    return 1;
}

int main(int argc, char** argv)
{
    if (argc != 2) {
        fprintf(stderr, "Usage: %s <empty-work-directory>\n", argv[0]);
        return 2;
    }

    assert(vessel_abi_version() == VESSEL_ABI_VERSION);
    vessel_options_t options = { 1, NULL, NULL };
    vessel_t storage = NULL;
    assert(vessel_init_ex(argv[1], &options, &storage) == VESSEL_SUCCESS);

    const char* key = "smoke/\xc3\xa9" "tude";
    vessel_object_info_t info = { "data.bin", ".bin", "application/octet-stream" };
    struct source input = { 900123, 0, 0 };
    vessel_metadata_result_t result = { 0 };
    assert(vessel_put_stream(storage, key, supply, &input, 0, info,
        VESSEL_COMPRESSION_LZ4, VESSEL_LEVEL_BALANCED, &result) == VESSEL_SUCCESS);
    assert(input.remaining == 0);
    assert(result.metadata.original_size == 900123);
    assert(result.metadata.compression_type == VESSEL_COMPRESSION_LZ4);
    assert(strcmp(result.metadata.key, key) == 0);
    assert(strlen(result.metadata.hash) == 66 && strcmp(result.metadata.hash + 64, "-2") == 0);
    assert(strcmp(result.metadata.etag, result.metadata.hash) == 0);
    assert(strcmp(result.metadata.file_name, "data.bin") == 0);
    assert(strcmp(result.metadata.extension, ".bin") == 0);
    assert(strcmp(result.metadata.mime_type, "application/octet-stream") == 0);
    int64_t written_at = result.creation_date_unix_ms;
    vessel_free_metadata(&result.metadata);

    int keys = 0;
    assert(vessel_get_all_keys_ex(storage, "smoke/", first_key, &keys) == VESSEL_SUCCESS);
    assert(keys == 1);

    vessel_stream_t stream = NULL;
    assert(vessel_stream_open(storage, key, &stream) == VESSEL_SUCCESS);
    assert(vessel_stream_get_metadata(stream, &result) == VESSEL_SUCCESS);
    assert(result.creation_date_unix_ms == written_at);
    vessel_free_metadata(&result.metadata);
    int32_t can_seek = -1;
    assert(vessel_stream_can_seek(stream, &can_seek) == VESSEL_SUCCESS && can_seek == 0);
    int64_t position = -1;
    assert(vessel_stream_position(stream, &position) == VESSEL_ERR_INVALID_OPERATION && position == 0);

    struct source cancelled = { 80000, 0, 1 };
    assert(vessel_put_stream(storage, key, supply, &cancelled, 1, info,
        VESSEL_COMPRESSION_NONE, VESSEL_LEVEL_BALANCED, &result) == VESSEL_ERR_CANCELLED);
    assert(result.metadata.key == NULL && result.creation_date_unix_ms == 0);

    int32_t deleted = 0;
    assert(vessel_delete(storage, key, &deleted) == VESSEL_SUCCESS && deleted == 1);
    assert(vessel_get_metadata_ex(storage, key, &result) == VESSEL_ERR_NOT_FOUND && result.metadata.key == NULL);

    const uint8_t plain[] = { 1, 23, 41, 8, 4 };
    assert(vessel_put_ex(storage, "raw", plain, sizeof(plain), 0, info,
        VESSEL_COMPRESSION_NONE, VESSEL_LEVEL_FASTEST, &result) == VESSEL_SUCCESS);
    assert(result.metadata.original_size == (int64_t)sizeof(plain));
    assert(result.metadata.compressed_size == (int64_t)sizeof(plain));
    assert(strlen(result.metadata.hash) == 64 && strcmp(result.metadata.etag, result.metadata.hash) == 0);
    vessel_free_metadata(&result.metadata);
    assert(vessel_put_ex(storage, "raw", plain, sizeof(plain), 0, info,
        VESSEL_COMPRESSION_NONE, VESSEL_LEVEL_FASTEST, &result) == VESSEL_ERR_ALREADY_EXISTS);
    assert(result.metadata.key == NULL && result.metadata.hash == NULL && result.creation_date_unix_ms == 0);
    vessel_stream_t seekable = NULL;
    assert(vessel_stream_open(storage, "raw", &seekable) == VESSEL_SUCCESS);
    assert(vessel_stream_can_seek(seekable, &can_seek) == VESSEL_SUCCESS && can_seek == 1);
    assert(vessel_stream_seek(seekable, -2, VESSEL_SEEK_END) == VESSEL_SUCCESS);
    assert(vessel_stream_position(seekable, &position) == VESSEL_SUCCESS && position == 3);
    vessel_stream_close(seekable);

    vessel_dispose(storage);
    vessel_dispose(storage);

    size_t total = 0;
    uint8_t buffer[4093];
    for (;;) {
        int32_t count = -1;
        assert(vessel_stream_read(stream, buffer, (int32_t)sizeof(buffer), &count) == VESSEL_SUCCESS);
        if (count == 0) break;
        for (int32_t index = 0; index < count; ++index)
            assert(buffer[index] == (uint8_t)((total + (size_t)index) % 251));
        total += (size_t)count;
    }
    assert(total == 900123);

    vessel_stream_close(stream);
    vessel_stream_close(stream);
    assert(vessel_stream_can_seek(stream, &can_seek) == VESSEL_ERR_DISPOSED && can_seek == 0);
    puts("Vessel C ABI: streaming, metadata, cancellation, snapshot and close passed.");
    return 0;
}
