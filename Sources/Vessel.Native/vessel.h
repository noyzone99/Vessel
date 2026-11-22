#ifndef VESSEL_H
#define VESSEL_H

#include <stddef.h>
#include <stdint.h>

#ifndef VESSEL_CALL
#  if defined(_WIN32)
#    define VESSEL_CALL __cdecl
#  else
#    define VESSEL_CALL
#  endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define VESSEL_ABI_VERSION 1
#define VESSEL_WHOLE_READ_LIMIT ((int64_t)64 * 1024 * 1024)

typedef void* vessel_t;
typedef void* vessel_stream_t;
typedef void* vessel_upload_t;

typedef int32_t vessel_error_t;
enum {
    VESSEL_SUCCESS = 0,
    VESSEL_ERR_UNKNOWN = -1,
    VESSEL_ERR_INVALID_ARGUMENT = -2,
    VESSEL_ERR_NOT_FOUND = -3,
    VESSEL_ERR_ALREADY_EXISTS = -4,
    VESSEL_ERR_IO = -5,
    VESSEL_ERR_DISPOSED = -6,
    VESSEL_ERR_INVALID_OPERATION = -7,
    VESSEL_ERR_CANCELLED = -8,
    VESSEL_ERR_LIMIT_EXCEEDED = -9,
    VESSEL_ERR_OUT_OF_MEMORY = -10
};

typedef int32_t vessel_compression_type_t;
enum {
    VESSEL_COMPRESSION_NONE = 0,
    VESSEL_COMPRESSION_ZSTD = 1,
    VESSEL_COMPRESSION_LZ4 = 2
};

typedef int32_t vessel_compression_level_t;
enum {
    VESSEL_LEVEL_FASTEST = 0,
    VESSEL_LEVEL_BALANCED = 1,
    VESSEL_LEVEL_HIGH = 2,
    VESSEL_LEVEL_ULTRA = 3
};

#define VESSEL_SEEK_SET 0
#define VESSEL_SEEK_CUR 1
#define VESSEL_SEEK_END 2

enum {
    VESSEL_LOG_DEBUG = 0,
    VESSEL_LOG_INFORMATION = 1,
    VESSEL_LOG_WARNING = 2,
    VESSEL_LOG_ERROR = 3
};

/* All strings are UTF-8, NUL-terminated. Text passed to callbacks is borrowed until the callback returns. */
typedef void (VESSEL_CALL *vessel_log_callback)(void* context, int32_t level, const char* message);
typedef void (VESSEL_CALL *vessel_key_callback)(const char* key);
typedef int32_t (VESSEL_CALL *vessel_key_callback_ex)(void* context, const char* key);
typedef int32_t (VESSEL_CALL *vessel_read_callback)(void* context, uint8_t* buffer, int32_t capacity);

typedef struct {
    int32_t enable_metadata_compression;   /* 0 or 1 */
    vessel_log_callback log;              /* NULL disables logging */
    void* log_context;
} vessel_options_t;

typedef struct {
    const char* file_name;
    const char* extension;
    const char* mime_type;
} vessel_object_info_t;

typedef struct {
    int64_t original_size;
    int64_t compressed_size;
    int32_t compression_type;
    int64_t creation_date_unix;            /* UTC seconds, retained for existing callers */
    char* key;
    char* hash;                            /* Blob identifier: SHA-256 with an optional codec suffix */
    char* etag;
    char* file_name;
    char* extension;
    char* mime_type;
} vessel_metadata_t;

typedef struct {
    vessel_metadata_t metadata;
    int64_t creation_date_unix_ms;        /* UTC milliseconds, matching the .NET API */
} vessel_metadata_result_t;

typedef struct {
    uint8_t* data;
    int64_t length;
    char* checksum;                       /* SHA-256 of the uncompressed payload */
    char* file_name;
    char* extension;
    char* mime_type;
} vessel_object_t;

/*
 * Output pointers must reference writable storage. Initialize an output to zero
 * before its first use and free owned results before reusing it. A failing call
 * leaves valid output storage zeroed. Free functions accept NULL and clear their
 * outputs. Free memory with the Vessel functions, never the host allocator.
 */
int32_t VESSEL_CALL vessel_abi_version(void);
vessel_error_t VESSEL_CALL vessel_init(const char* path, vessel_t* out_handle);
vessel_error_t VESSEL_CALL vessel_init_ex(const char* path, const vessel_options_t* options, vessel_t* out_handle);

/*
 * Closes are idempotent. In-flight calls complete using their original handle.
 * Wait for those calls before releasing any callback context or reopening the
 * same directory. Storage logging may run on library threads. The logging
 * callback and its context must be safe to use on those threads.
 */
void VESSEL_CALL vessel_dispose(vessel_t handle);

vessel_error_t VESSEL_CALL vessel_put(vessel_t handle, const char* key, const uint8_t* data,
    int32_t length, int32_t overwrite, vessel_object_info_t info, int32_t comp_type, int32_t comp_level);
/* Optional write metadata storage is allocated before the object is published. */
vessel_error_t VESSEL_CALL vessel_put_ex(vessel_t handle, const char* key, const uint8_t* data,
    int64_t length, int32_t overwrite, vessel_object_info_t info, int32_t comp_type, int32_t comp_level,
    vessel_metadata_result_t* out_metadata);

/*
 * The input callback runs synchronously on the calling thread. Write up to
 * capacity bytes and return the count, 0 at EOF, VESSEL_ERR_CANCELLED to cancel,
 * or another negative value for I/O failure. Do not retain the buffer. A
 * cancelled upload preserves any existing object. The metadata output is
 * optional and allocated before publication. Free an owned result with
 * vessel_free_metadata(&result.metadata).
 */
vessel_error_t VESSEL_CALL vessel_put_stream(vessel_t handle, const char* key, vessel_read_callback read,
    void* context, int32_t overwrite, vessel_object_info_t info, int32_t comp_type, int32_t comp_level,
    vessel_metadata_result_t* out_metadata);

/*
 * Push upload. On successful begin, complete is called exactly once, possibly
 * before begin returns or on a library thread. Metadata is borrowed until the
 * callback returns and is NULL on failure. Keep context alive until then.
 * Begin allocates the result storage. If begin fails, no callback runs.
 * The callback must not reenter the upload. Write copies up to 64 KiB before
 * returning. If out_paused is nonzero, call drain before the next write or
 * commit. Drain may block. Serialize producer calls. End with commit == 0
 * can also interrupt a pending drain. End releases the handle,
 * is idempotent and is required even if completion already reported an error.
 */
typedef void (VESSEL_CALL *vessel_upload_callback)(void* context, vessel_error_t status,
    const vessel_metadata_result_t* metadata);
vessel_error_t VESSEL_CALL vessel_upload_begin(vessel_t handle, const char* key, int32_t overwrite,
    vessel_object_info_t info, int32_t comp_type, int32_t comp_level, vessel_upload_callback complete,
    void* context, vessel_upload_t* out_upload);
vessel_error_t VESSEL_CALL vessel_upload_write(vessel_upload_t upload, const uint8_t* data, int32_t count,
    int32_t* out_paused);
vessel_error_t VESSEL_CALL vessel_upload_drain(vessel_upload_t upload);
void VESSEL_CALL vessel_upload_end(vessel_upload_t upload, int32_t commit);

/* Reads the entire payload. Use vessel_stream_* above VESSEL_WHOLE_READ_LIMIT. */
vessel_error_t VESSEL_CALL vessel_read(vessel_t handle, const char* key, vessel_object_t* out_obj);
vessel_error_t VESSEL_CALL vessel_get_metadata(vessel_t handle, const char* key, vessel_metadata_t* out_metadata);
vessel_error_t VESSEL_CALL vessel_get_metadata_ex(vessel_t handle, const char* key, vessel_metadata_result_t* out_metadata);
vessel_error_t VESSEL_CALL vessel_delete(vessel_t handle, const char* key, int32_t* out_deleted);

/* A NULL or empty prefix enumerates all keys. Callbacks run on the calling thread. */
vessel_error_t VESSEL_CALL vessel_get_all_keys(vessel_t handle, const char* prefix, vessel_key_callback callback);
/* Callback returns 0 to continue, positive to stop successfully, negative to cancel. */
vessel_error_t VESSEL_CALL vessel_get_all_keys_ex(vessel_t handle, const char* prefix, vessel_key_callback_ex callback, void* context);

/*
 * Each stream owns its read position and metadata snapshot. An opened stream
 * remains readable after an overwrite or delete. Operations on the same stream
 * are serialized, so use separate handles for independent readers.
 */
vessel_error_t VESSEL_CALL vessel_stream_open(vessel_t handle, const char* key, vessel_stream_t* out_stream);
vessel_error_t VESSEL_CALL vessel_stream_read(vessel_stream_t stream, uint8_t* buffer, int32_t count, int32_t* out_bytes_read);
vessel_error_t VESSEL_CALL vessel_stream_length(vessel_stream_t stream, int64_t* out_length);
vessel_error_t VESSEL_CALL vessel_stream_get_metadata(vessel_stream_t stream, vessel_metadata_result_t* out_metadata);
vessel_error_t VESSEL_CALL vessel_stream_can_seek(vessel_stream_t stream, int32_t* out_can_seek);
/* Position and seek return VESSEL_ERR_INVALID_OPERATION on compressed streams. */
vessel_error_t VESSEL_CALL vessel_stream_position(vessel_stream_t stream, int64_t* out_position);
vessel_error_t VESSEL_CALL vessel_stream_seek(vessel_stream_t stream, int64_t offset, int32_t origin);
void VESSEL_CALL vessel_stream_close(vessel_stream_t stream);

void VESSEL_CALL vessel_free_object(vessel_object_t* obj);
void VESSEL_CALL vessel_free_metadata(vessel_metadata_t* metadata);

#ifdef __cplusplus
}
#endif
#endif
