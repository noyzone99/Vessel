#include <node_api.h>
#include "../../../Sources/Vessel.Native/vessel.h"

#include <atomic>
#include <condition_variable>
#include <cstring>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace {

#define VESSEL_SYMBOLS(X) \
    X(vessel_abi_version) X(vessel_init_ex) X(vessel_dispose) X(vessel_delete) \
    X(vessel_get_metadata_ex) X(vessel_get_all_keys_ex) X(vessel_free_metadata) \
    X(vessel_stream_open) X(vessel_stream_read) X(vessel_stream_close) \
    X(vessel_upload_begin) X(vessel_upload_write) X(vessel_upload_drain) X(vessel_upload_end)

struct Api {
#define FIELD(name) decltype(&::name) name = nullptr;
    VESSEL_SYMBOLS(FIELD)
#undef FIELD
};

Api api;
std::mutex load_mutex;

struct NapiFailure {};

void Check(napi_status status) {
    if (status != napi_ok) throw NapiFailure{};
}

napi_value Undefined(napi_env env) {
    napi_value value;
    Check(napi_get_undefined(env, &value));
    return value;
}

napi_value String(napi_env env, const char* text) {
    napi_value value;
    if (text) Check(napi_create_string_utf8(env, text, NAPI_AUTO_LENGTH, &value));
    else Check(napi_get_null(env, &value));
    return value;
}

napi_value Number(napi_env env, int32_t number) {
    napi_value value;
    Check(napi_create_int32(env, number, &value));
    return value;
}

napi_value BigInt(napi_env env, int64_t number) {
    napi_value value;
    Check(napi_create_bigint_int64(env, number, &value));
    return value;
}

napi_value Boolean(napi_env env, bool boolean) {
    napi_value value;
    Check(napi_get_boolean(env, boolean, &value));
    return value;
}

std::string Text(napi_env env, napi_value value) {
    size_t size;
    Check(napi_get_value_string_utf8(env, value, nullptr, 0, &size));
    std::string text(size, '\0');
    Check(napi_get_value_string_utf8(env, value, text.data(), size + 1, &size));
    if (text.find('\0') != std::string::npos) throw std::invalid_argument("Vessel text contains a NUL byte");
    return text;
}

bool Bool(napi_env env, napi_value value) {
    bool result;
    Check(napi_get_value_bool(env, value, &result));
    return result;
}

int32_t Int(napi_env env, napi_value value) {
    int32_t result;
    Check(napi_get_value_int32(env, value, &result));
    return result;
}

struct Args {
    napi_value values[8]{};
    void* data = nullptr;

    Args(napi_env env, napi_callback_info info, size_t count) {
        const auto expected = count;
        Check(napi_get_cb_info(env, info, &count, values, nullptr, &data));
        if (count != expected) throw std::invalid_argument("Invalid Vessel addon arguments");
    }

    napi_value operator[](size_t index) const { return values[index]; }
};

template<napi_value (*Function)(napi_env, napi_callback_info)>
napi_value Entry(napi_env env, napi_callback_info info) {
    try { return Function(env, info); }
    catch (const std::invalid_argument& error) { napi_throw_type_error(env, nullptr, error.what()); }
    catch (const std::exception& error) { napi_throw_error(env, nullptr, error.what()); }
    catch (const NapiFailure&) {
        bool pending = false;
        napi_is_exception_pending(env, &pending);
        if (!pending) napi_throw_error(env, nullptr, "Vessel Node-API call failed");
    }
    return nullptr;
}

napi_value Load(napi_env env, napi_callback_info info) {
    Args args(env, info, 1);
    std::lock_guard guard(load_mutex);
    if (api.vessel_abi_version) return Undefined(env);

#ifdef _WIN32
    HMODULE addon_module;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&Load), &addon_module))
        throw std::runtime_error("Cannot retain the Vessel addon");

    size_t size;
    Check(napi_get_value_string_utf16(env, args[0], nullptr, 0, &size));
    std::u16string path(size, u'\0');
    Check(napi_get_value_string_utf16(env, args[0], path.data(), size + 1, &size));
    std::wstring wide_path(path.begin(), path.end());
    const auto library = LoadLibraryExW(wide_path.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!library) throw std::runtime_error("Cannot load Vessel library (Windows error " + std::to_string(GetLastError()) + ")");
    auto symbol = [library](const char* name) { return GetProcAddress(library, name); };
#else
    Dl_info addon_image{};
    if (!dladdr(reinterpret_cast<const void*>(&Load), &addon_image) ||
        !dlopen(addon_image.dli_fname, RTLD_NOW | RTLD_LOCAL))
        throw std::runtime_error("Cannot retain the Vessel addon");

    const auto path = Text(env, args[0]);
    const auto library = dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (!library) throw std::runtime_error(dlerror());
    auto symbol = [library](const char* name) { return dlsym(library, name); };
#endif
    // Managed completion callbacks can return after a Node worker has shut down.
    // Keep both the addon and the NativeAOT runtime loaded for the process lifetime.
    Api loaded;
#define BIND(name) \
    loaded.name = reinterpret_cast<decltype(loaded.name)>(symbol(#name)); \
    if (!loaded.name) throw std::runtime_error("Missing native Vessel symbol: " #name);
    VESSEL_SYMBOLS(BIND)
#undef BIND
    if (loaded.vessel_abi_version() != VESSEL_ABI_VERSION) throw std::runtime_error("Unsupported Vessel ABI version");
    api = loaded;
    return Undefined(env);
}

enum class Kind { Store, Reader, Upload };

struct Handle {
    const Kind kind;
    std::atomic<void*> value{nullptr};

    explicit Handle(Kind kind) : kind(kind) {}
    ~Handle() { Close(); }

    void Close(bool commit = false) noexcept {
        auto raw = value.exchange(nullptr);
        if (!raw) return;
        switch (kind) {
            case Kind::Store: api.vessel_dispose(raw); break;
            case Kind::Reader: api.vessel_stream_close(raw); break;
            case Kind::Upload: api.vessel_upload_end(raw, commit); break;
        }
    }
};

using SharedHandle = std::shared_ptr<Handle>;

napi_value Wrap(napi_env env, const SharedHandle& handle) {
    auto owner = std::make_unique<SharedHandle>(handle);
    napi_value value;
    Check(napi_create_external(env, owner.get(), [](napi_env, void* data, void*) {
        delete static_cast<SharedHandle*>(data);
    }, nullptr, &value));
    owner.release();
    return value;
}

SharedHandle Unwrap(napi_env env, napi_value value, Kind kind) {
    void* data;
    Check(napi_get_value_external(env, value, &data));
    auto handle = *static_cast<SharedHandle*>(data);
    if (handle->kind != kind) throw std::invalid_argument("Invalid Vessel handle");
    return handle;
}

napi_value Error(napi_env env, vessel_error_t status) {
    napi_value error;
    Check(napi_create_error(env, nullptr, String(env, "Vessel operation failed"), &error));
    Check(napi_set_named_property(env, error, "status", Number(env, status)));
    return error;
}

napi_value Metadata(napi_env env, const vessel_metadata_result_t& result) {
    const auto& meta = result.metadata;
    napi_value object;
    Check(napi_create_object(env, &object));
    auto set = [env, object](const char* name, napi_value value) {
        Check(napi_set_named_property(env, object, name, value));
    };
    set("key", String(env, meta.key));
    set("hash", String(env, meta.hash));
    set("etag", String(env, meta.etag));
    set("originalSize", BigInt(env, meta.original_size));
    set("compressedSize", BigInt(env, meta.compressed_size));
    set("compression", Number(env, meta.compression_type));
    napi_value date;
    Check(napi_create_date(env, static_cast<double>(result.creation_date_unix_ms), &date));
    set("createdAt", date);
    set("fileName", String(env, meta.file_name));
    set("extension", String(env, meta.extension));
    set("mimeType", String(env, meta.mime_type));
    return object;
}

enum class Work { Open, Close, OpenReader, Read, Metadata, Delete, Keys, Drain };

struct Job {
    napi_env env;
    Work work;
    napi_async_work request = nullptr;
    napi_deferred promise = nullptr;
    napi_ref buffer = nullptr;
    SharedHandle handle;
    SharedHandle opened;
    std::string text;
    void* bytes = nullptr;
    int32_t count = 0;
    int32_t result = 0;
    vessel_error_t status = VESSEL_SUCCESS;
    vessel_metadata_result_t metadata{};
    std::vector<std::string> keys;

    Job(napi_env env, Work work) : env(env), work(work) {}

    ~Job() {
        if (buffer) napi_delete_reference(env, buffer);
        if (request) napi_delete_async_work(env, request);
        if (work == Work::Metadata) api.vessel_free_metadata(&metadata.metadata);
    }

    void Execute() noexcept {
        auto raw = handle ? handle->value.load() : nullptr;
        if (handle && !raw && work != Work::Close) {
            status = VESSEL_ERR_DISPOSED;
            return;
        }
        void* output = nullptr;
        switch (work) {
            case Work::Open: {
                vessel_options_t options{count, nullptr, nullptr};
                status = api.vessel_init_ex(text.c_str(), &options, &output);
                opened->value = output;
                break;
            }
            case Work::Close: handle->Close(); break;
            case Work::OpenReader:
                status = api.vessel_stream_open(raw, text.c_str(), &output);
                opened->value = output;
                break;
            case Work::Read:
                status = api.vessel_stream_read(raw, static_cast<uint8_t*>(bytes), count, &result);
                break;
            case Work::Metadata: status = api.vessel_get_metadata_ex(raw, text.c_str(), &metadata); break;
            case Work::Delete: status = api.vessel_delete(raw, text.c_str(), &result); break;
            case Work::Keys: {
                auto collect = [](void* context, const char* key) -> int32_t {
                    try { static_cast<Job*>(context)->keys.emplace_back(key); }
                    catch (...) { return -1; }
                    return 0;
                };
                status = api.vessel_get_all_keys_ex(raw, text.c_str(), collect, this);
                if (status == VESSEL_ERR_CANCELLED) status = VESSEL_ERR_OUT_OF_MEMORY;
                break;
            }
            case Work::Drain: status = api.vessel_upload_drain(raw); break;
        }
    }

    napi_value Value() {
        switch (work) {
            case Work::Open:
            case Work::OpenReader: return Wrap(env, opened);
            case Work::Read: return Number(env, result);
            case Work::Metadata: return Metadata(env, metadata);
            case Work::Delete: return Boolean(env, result != 0);
            case Work::Keys: {
                napi_value array;
                Check(napi_create_array_with_length(env, keys.size(), &array));
                for (uint32_t i = 0; i < keys.size(); ++i)
                    Check(napi_set_element(env, array, i, String(env, keys[i].c_str())));
                return array;
            }
            default: return Undefined(env);
        }
    }

    static napi_value Queue(std::unique_ptr<Job> job) {
        auto env = job->env;
        napi_value promise;
        Check(napi_create_promise(env, &job->promise, &promise));
        Check(napi_create_async_work(env, nullptr, String(env, "Vessel"), [](napi_env, void* data) {
            static_cast<Job*>(data)->Execute();
        }, [](napi_env env, napi_status status, void* data) {
            std::unique_ptr<Job> job(static_cast<Job*>(data));
            if (status != napi_ok)
                job->status = status == napi_cancelled ? VESSEL_ERR_CANCELLED : VESSEL_ERR_UNKNOWN;
            try {
                if (job->status == VESSEL_SUCCESS) Check(napi_resolve_deferred(env, job->promise, job->Value()));
                else Check(napi_reject_deferred(env, job->promise, Error(env, job->status)));
            } catch (const NapiFailure&) {
                napi_value error;
                bool pending = false;
                napi_is_exception_pending(env, &pending);
                if (pending && napi_get_and_clear_last_exception(env, &error) == napi_ok)
                    napi_reject_deferred(env, job->promise, error);
            }
        }, job.get(), &job->request));
        Check(napi_queue_async_work(env, job->request));
        job.release();
        return promise;
    }
};

napi_value Open(napi_env env, napi_callback_info info) {
    Args args(env, info, 2);
    auto job = std::make_unique<Job>(env, Work::Open);
    job->text = Text(env, args[0]);
    job->count = Bool(env, args[1]);
    job->opened = std::make_shared<Handle>(Kind::Store);
    return Job::Queue(std::move(job));
}

napi_value StoreJob(napi_env env, napi_callback_info info) {
    Args args(env, info, 2);
    const auto work = *static_cast<const Work*>(args.data);
    auto job = std::make_unique<Job>(env, work);
    job->handle = Unwrap(env, args[0], Kind::Store);
    job->text = Text(env, args[1]);
    if (work == Work::OpenReader) job->opened = std::make_shared<Handle>(Kind::Reader);
    return Job::Queue(std::move(job));
}

napi_value Close(napi_env env, napi_callback_info info) {
    Args args(env, info, 1);
    auto job = std::make_unique<Job>(env, Work::Close);
    job->handle = Unwrap(env, args[0], *static_cast<const Kind*>(args.data));
    return Job::Queue(std::move(job));
}

napi_value Read(napi_env env, napi_callback_info info) {
    Args args(env, info, 2);
    auto job = std::make_unique<Job>(env, Work::Read);
    job->handle = Unwrap(env, args[0], Kind::Reader);
    size_t length;
    Check(napi_get_buffer_info(env, args[1], &job->bytes, &length));
    if (length == 0 || length > INT32_MAX) throw std::invalid_argument("Invalid read buffer size");
    job->count = static_cast<int32_t>(length);
    // The JS wrapper owns this private buffer until the read resolves.
    Check(napi_create_reference(env, args[1], 1, &job->buffer));
    return Job::Queue(std::move(job));
}

struct UploadNotice {
    SharedHandle handle = std::make_shared<Handle>(Kind::Upload);
    napi_threadsafe_function notify = nullptr;
    std::mutex mutex;
    std::condition_variable done;
    bool delivered = false;
    bool closing = false;
    bool cleanup_registered = false;
    vessel_error_t status = VESSEL_SUCCESS;
    vessel_metadata_result_t metadata{};
    std::string key, hash, etag, name, extension, mime;

    void Wait() {
        std::unique_lock lock(mutex);
        done.wait(lock, [this] { return delivered; });
    }

    static void Cleanup(void* data) {
        auto notice = static_cast<UploadNotice*>(data);
        notice->cleanup_registered = false;
        {
            std::lock_guard lock(notice->mutex);
            notice->closing = true;
        }
        notice->handle->Close();
        notice->Wait();
    }

    static void Finalize(napi_env env, void* data, void*) {
        auto notice = static_cast<UploadNotice*>(data);
        notice->Wait();
        if (env && notice->cleanup_registered) napi_remove_env_cleanup_hook(env, Cleanup, notice);
        delete notice;
    }

    static char* Copy(std::string& target, const char* source) {
        if (!source) return nullptr;
        target = source;
        return target.data();
    }

    static void Complete(void* context, vessel_error_t status, const vessel_metadata_result_t* metadata) noexcept {
        auto notice = static_cast<UploadNotice*>(context);
        std::lock_guard lock(notice->mutex);
        notice->status = status;
        if (status == VESSEL_SUCCESS) {
            try {
                notice->metadata = *metadata;
                auto& meta = notice->metadata.metadata;
                meta.key = Copy(notice->key, meta.key);
                meta.hash = Copy(notice->hash, meta.hash);
                meta.etag = Copy(notice->etag, meta.etag);
                meta.file_name = Copy(notice->name, meta.file_name);
                meta.extension = Copy(notice->extension, meta.extension);
                meta.mime_type = Copy(notice->mime, meta.mime_type);
            } catch (...) { notice->status = VESSEL_ERR_OUT_OF_MEMORY; }
        }
        if (!notice->closing) {
            const auto queued = napi_call_threadsafe_function(notice->notify, nullptr, napi_tsfn_nonblocking);
            if (queued != napi_closing) napi_release_threadsafe_function(notice->notify, napi_tsfn_release);
        }
        notice->delivered = true;
        notice->done.notify_all();
    }

    static void Call(napi_env env, napi_value callback, void* context, void*) {
        auto notice = static_cast<UploadNotice*>(context);
        notice->Wait();
        notice->handle->Close();
        if (!env || !callback) return;
        try {
            napi_value args[2];
            args[0] = notice->status == VESSEL_SUCCESS ? Undefined(env) : Error(env, notice->status);
            args[1] = notice->status == VESSEL_SUCCESS ? Metadata(env, notice->metadata) : Undefined(env);
            napi_call_function(env, Undefined(env), callback, 2, args, nullptr);
        } catch (const NapiFailure&) {
            napi_value error;
            bool pending = false;
            napi_is_exception_pending(env, &pending);
            napi_value receiver;
            if (pending && napi_get_and_clear_last_exception(env, &error) == napi_ok &&
                napi_get_undefined(env, &receiver) == napi_ok)
                napi_call_function(env, receiver, callback, 1, &error, nullptr);
        }
    }
};

napi_value Begin(napi_env env, napi_callback_info info) {
    Args args(env, info, 7);
    auto store = Unwrap(env, args[0], Kind::Store);
    const auto key = Text(env, args[1]);
    const auto overwrite = Bool(env, args[2]);
    const auto codec = Int(env, args[3]);
    const auto level = Int(env, args[4]);
    bool has_text[3]{};
    std::string text[3];
    bool is_array;
    Check(napi_is_array(env, args[5], &is_array));
    if (!is_array) throw std::invalid_argument("Invalid Vessel metadata");
    for (uint32_t i = 0; i < 3; ++i) {
        napi_value value;
        napi_valuetype type;
        Check(napi_get_element(env, args[5], i, &value));
        Check(napi_typeof(env, value, &type));
        has_text[i] = type == napi_string;
        if (has_text[i]) text[i] = Text(env, value);
    }
    const vessel_object_info_t metadata{
        has_text[0] ? text[0].c_str() : nullptr,
        has_text[1] ? text[1].c_str() : nullptr,
        has_text[2] ? text[2].c_str() : nullptr
    };
    auto notice = std::make_unique<UploadNotice>();
    auto wrapped = Wrap(env, notice->handle);
    auto name = String(env, "Vessel upload");
    Check(napi_create_threadsafe_function(env, args[6], nullptr, name, 0, 1, notice.get(),
        UploadNotice::Finalize, notice.get(), UploadNotice::Call, &notice->notify));
    auto context = notice.release();
    const auto cleanup = napi_add_env_cleanup_hook(env, UploadNotice::Cleanup, context);
    if (cleanup != napi_ok) {
        UploadNotice::Complete(context, VESSEL_ERR_UNKNOWN, nullptr);
        Check(cleanup);
    }
    context->cleanup_registered = true;
    vessel_upload_t handle = nullptr;
    auto status = api.vessel_upload_begin(store->value.load(), key.c_str(), overwrite, metadata, codec, level,
        UploadNotice::Complete, context, &handle);
    context->handle->value = handle;
    if (status != VESSEL_SUCCESS) UploadNotice::Complete(context, status, nullptr);
    return wrapped;
}

napi_value Write(napi_env env, napi_callback_info info) {
    Args args(env, info, 4);
    auto handle = Unwrap(env, args[0], Kind::Upload);
    void* data;
    size_t size;
    Check(napi_get_buffer_info(env, args[1], &data, &size));
    double offset;
    Check(napi_get_value_double(env, args[2], &offset));
    const auto count = Int(env, args[3]);
    if (!(offset >= 0 && count > 0 && offset + count <= size))
        throw std::invalid_argument("Invalid write buffer range");
    int32_t paused;
    auto raw = handle->value.load();
    const auto status = raw ? api.vessel_upload_write(raw,
        static_cast<uint8_t*>(data) + static_cast<size_t>(offset), count, &paused) : VESSEL_ERR_DISPOSED;
    if (status != VESSEL_SUCCESS) {
        Check(napi_throw(env, Error(env, status)));
        return nullptr;
    }
    return Boolean(env, paused != 0);
}

napi_value Drain(napi_env env, napi_callback_info info) {
    Args args(env, info, 1);
    auto job = std::make_unique<Job>(env, Work::Drain);
    job->handle = Unwrap(env, args[0], Kind::Upload);
    return Job::Queue(std::move(job));
}

napi_value End(napi_env env, napi_callback_info info) {
    Args args(env, info, 2);
    Unwrap(env, args[0], Kind::Upload)->Close(Bool(env, args[1]));
    return Undefined(env);
}

Work open_reader = Work::OpenReader, metadata = Work::Metadata, delete_object = Work::Delete, keys = Work::Keys;
Kind store = Kind::Store, reader = Kind::Reader;

} // namespace

NAPI_MODULE_INIT() {
    const napi_property_descriptor methods[] = {
        {"load", nullptr, Entry<Load>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"open", nullptr, Entry<Open>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"close", nullptr, Entry<Close>, nullptr, nullptr, nullptr, napi_default, &store},
        {"openReader", nullptr, Entry<StoreJob>, nullptr, nullptr, nullptr, napi_default, &open_reader},
        {"closeReader", nullptr, Entry<Close>, nullptr, nullptr, nullptr, napi_default, &reader},
        {"read", nullptr, Entry<Read>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"metadata", nullptr, Entry<StoreJob>, nullptr, nullptr, nullptr, napi_default, &metadata},
        {"delete", nullptr, Entry<StoreJob>, nullptr, nullptr, nullptr, napi_default, &delete_object},
        {"keys", nullptr, Entry<StoreJob>, nullptr, nullptr, nullptr, napi_default, &keys},
        {"begin", nullptr, Entry<Begin>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"write", nullptr, Entry<Write>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"drain", nullptr, Entry<Drain>, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"end", nullptr, Entry<End>, nullptr, nullptr, nullptr, napi_default, nullptr}
    };
    if (napi_define_properties(env, exports, sizeof(methods) / sizeof(methods[0]), methods) != napi_ok) {
        napi_throw_error(env, nullptr, "Cannot initialize Vessel addon");
        return nullptr;
    }
    return exports;
}
