/**
 * @file vessel.h
 * @brief Vessel Storage Native API - High-performance local blob storage.
 *
 * Compiled via .NET NativeAOT. All functions return a vessel_error_t status.
 */

#ifndef VESSEL_H
#define VESSEL_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

    /**
     * @brief Opaque handle representing a VesselStorage instance.
     */
    typedef void* vessel_t;

    /**
     * @brief Opaque handle representing an open read stream.
     */
    typedef void* vessel_stream_t;

    /**
     * @brief Status codes for Vessel operations.
     */
    typedef enum {
        VESSEL_SUCCESS = 0,               /**< Operation completed successfully. */
        VESSEL_ERR_UNKNOWN = -1,          /**< An unexpected managed exception occurred. */
        VESSEL_ERR_INVALID_ARGUMENT = -2, /**< Null or invalid arguments provided. */
        VESSEL_ERR_NOT_FOUND = -3,         /**< The requested key does not exist. */
        VESSEL_ERR_ALREADY_EXISTS = -4,    /**< Overwrite was false and the key exists. */
        VESSEL_ERR_IO = -5,                /**< Disk or network I/O failure. */
        VESSEL_ERR_DISPOSED = -6,          /**< The storage instance has been closed. */
        VESSEL_ERR_INVALID_OPERATION = -7  /**< Logic error or invalid state. */
    } vessel_error_t;

    /**
     * @brief Compression algorithms supported by Vessel.
     */
    typedef enum {
        VESSEL_COMPRESSION_NONE = 0,
        VESSEL_COMPRESSION_ZSTD = 1,
        VESSEL_COMPRESSION_LZ4 = 2
    } vessel_compression_type_t;

    /**
     * @brief Compression intensity levels.
     */
    typedef enum {
        VESSEL_LEVEL_FASTEST = 0,
        VESSEL_LEVEL_BALANCED = 1,
        VESSEL_LEVEL_HIGH = 2,
        VESSEL_LEVEL_ULTRA = 3
    } vessel_compression_level_t;

    /**
     * @brief Seek origins for vessel_stream_seek().
     *
     * Mirror the C standard library SEEK_* constants.
     */
#define VESSEL_SEEK_SET 0 /**< Seek from the beginning of the stream. */
#define VESSEL_SEEK_CUR 1 /**< Seek from the current position.        */
#define VESSEL_SEEK_END 2 /**< Seek from the end of the stream.       */

     /**
      * @brief Input structure for storing object properties.
      * Maps to ObjectInfo in C#.
      */
    typedef struct {
        const char* file_name;      /**< Optional original file name. */
        const char* extension;      /**< Optional file extension (e.g., ".json"). */
        const char* mime_type;      /**< Optional MIME type (e.g., "application/json"). */
    } vessel_object_info_t;

    /**
     * @brief Full metadata for an object stored in Vessel.
     *
     * IMPORTANT: This structure contains heap-allocated strings.
     * MUST be freed by calling vessel_free_metadata().
     */
    typedef struct {
        int64_t original_size;      /**< Size of the payload before compression. */
        int64_t compressed_size;    /**< Actual size occupied on disk. */
        int32_t compression_type;   /**< Algorithm used to store the blob. */
        int64_t creation_date_unix; /**< UTC Timestamp (Unix Seconds). */
        char* key;                /**< The logical key unique to this object. */
        char* hash;               /**< SHA256 content-based identifier. */
        char* etag;               /**< Versioning tag for cache validation. */
        char* file_name;          /**< Metadata: Original file name. */
        char* extension;          /**< Metadata: Original file extension. */
        char* mime_type;          /**< Metadata: Content MIME type. */
    } vessel_metadata_t;

    /**
     * @brief Combined data and essential metadata for a retrieved object.
     *
     * IMPORTANT: Contains a heap-allocated buffer and strings.
     * MUST be freed by calling vessel_free_object().
     */
    typedef struct {
        uint8_t* data;              /**< The uncompressed payload buffer. */
        int64_t  length;            /**< Size of the data buffer. */
        char* checksum;          /**< SHA256 hash of the content. */
        char* file_name;         /**< Metadata: Original file name. */
        char* extension;         /**< Metadata: Original file extension. */
        char* mime_type;         /**< Metadata: Content MIME type. */
    } vessel_object_t;

    /**
     * @brief Callback signature for key enumeration.
     * @param key A null-terminated UTF-8 string. The pointer is valid only
     *            for the duration of the callback invocation; copy it if
     *            you need to keep it.
     */
    typedef void (*vessel_key_callback)(const char* key);

    /**
     * @brief Initializes a VesselStorage instance.
     *
     * @param path The filesystem path to the storage root.
     * @param out_handle Pointer to the handle to be initialized.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_init(const char* path, vessel_t* out_handle);

    /**
     * @brief Writes data to the storage engine.
     *
     * @param handle The storage instance handle.
     * @param key The logical key.
     * @param data Buffer containing the bytes to store.
     * @param length Size of the data buffer.
     * @param overwrite 1 to allow replacing existing keys, 0 to fail.
     * @param info Optional metadata (file name, ext, mime).
     * @param comp_type Algorithm (None, Zstd, LZ4).
     * @param comp_level Compression strength (Fastest to Ultra).
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_put(
        vessel_t handle,
        const char* key,
        const uint8_t* data,
        int32_t length,
        int32_t overwrite,
        vessel_object_info_t info,
        int32_t comp_type,
        int32_t comp_level);

    /**
     * @brief Reads an entire object's payload and essential metadata into memory.
     *
     * This is a convenience helper for small blobs. For large objects,
     * use the streaming API (vessel_stream_open / vessel_stream_read) to
     * avoid allocating the full payload in a single contiguous buffer.
     *
     * @param handle The storage handle.
     * @param key The key to look up.
     * @param out_obj Pointer to a structure that will be filled with data and pointers.
     * @return VESSEL_SUCCESS or VESSEL_ERR_NOT_FOUND.
     */
    vessel_error_t vessel_read(vessel_t handle, const char* key, vessel_object_t* out_obj);

    /**
     * @brief Retrieves full technical metadata for an object without reading the blob.
     *
     * @param handle The storage handle.
     * @param key The key to look up.
     * @param out_metadata Pointer to a structure that will be filled with metadata.
     * @return VESSEL_SUCCESS or VESSEL_ERR_NOT_FOUND.
     */
    vessel_error_t vessel_get_metadata(vessel_t handle, const char* key, vessel_metadata_t* out_metadata);

    /**
     * @brief Deletes an object and its associated blob if no longer referenced.
     *
     * @param handle The storage handle.
     * @param key The key to delete.
     * @param out_deleted Pointer to receive 1 if deleted, 0 if it didn't exist.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_delete(vessel_t handle, const char* key, int32_t* out_deleted);

    /**
     * @brief Iterates through all keys matching the given prefix.
     *
     * @param handle The storage handle.
     * @param prefix Prefix to filter keys (use "" for all keys).
     * @param callback Function called for every key found.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_get_all_keys(vessel_t handle, const char* prefix, vessel_key_callback callback);

    /* ==================================================================== */
    /* Streaming API                                                        */
    /* ==================================================================== */

    /**
     * @brief Opens a zero-copy read stream for the specified key.
     *
     * The returned stream handle must be closed with vessel_stream_close().
     * Multiple independent streams may be opened concurrently.
     *
     * @param handle The storage instance handle.
     * @param key The logical key to open.
     * @param out_stream Pointer to receive the stream handle.
     * @return VESSEL_SUCCESS or VESSEL_ERR_NOT_FOUND.
     */
    vessel_error_t vessel_stream_open(vessel_t handle, const char* key, vessel_stream_t* out_stream);

    /**
     * @brief Reads up to @p count bytes from the stream into @p buffer.
     *
     * This is a direct, zero-copy read into the caller-supplied buffer.
     * When the end of the stream is reached, @p out_bytes_read is set to 0
     * and VESSEL_SUCCESS is still returned.
     *
     * @param stream The stream handle obtained from vessel_stream_open().
     * @param buffer Caller-allocated buffer to receive the bytes.
     * @param count Maximum number of bytes to read.
     * @param out_bytes_read Pointer to receive the actual number of bytes read.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_stream_read(
        vessel_stream_t stream,
        uint8_t* buffer,
        int32_t count,
        int32_t* out_bytes_read);

    /**
     * @brief Returns the total length of the stream in bytes.
     *
     * @param stream The stream handle.
     * @param out_length Pointer to receive the length.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_stream_length(vessel_stream_t stream, int64_t* out_length);

    /**
     * @brief Returns the current position within the stream.
     *
     * @param stream The stream handle.
     * @param out_position Pointer to receive the position.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_stream_position(vessel_stream_t stream, int64_t* out_position);

    /**
     * @brief Repositions the stream offset.
     *
     * @param stream The stream handle.
     * @param offset Byte offset relative to @p origin.
     * @param origin One of VESSEL_SEEK_SET, VESSEL_SEEK_CUR, or VESSEL_SEEK_END.
     * @return VESSEL_SUCCESS or error code.
     */
    vessel_error_t vessel_stream_seek(vessel_stream_t stream, int64_t offset, int32_t origin);

    /**
     * @brief Closes the stream and releases all associated resources.
     *
     * @param stream The stream handle. Safe to call with NULL (no-op).
     */
    void vessel_stream_close(vessel_stream_t stream);

    /* ==================================================================== */
    /* Cleanup helpers                                                      */
    /* ==================================================================== */

    /**
     * @brief Frees the payload buffer and strings inside a vessel_object_t.
     *
     * All pointer fields inside the structure are set to NULL after freeing,
     * preventing accidental double-free.
     *
     * @param obj Pointer to the object structure. Safe to call with NULL (no-op).
     */
    void vessel_free_object(vessel_object_t* obj);

    /**
     * @brief Frees all heap-allocated string members inside a vessel_metadata_t.
     *
     * All pointer fields inside the structure are set to NULL after freeing,
     * preventing accidental double-free.
     *
     * @param metadata Pointer to the metadata structure. Safe to call with NULL (no-op).
     */
    void vessel_free_metadata(vessel_metadata_t* metadata);

    /**
     * @brief Closes the storage engine and releases the handle.
     *
     * @param handle The storage handle. Safe to call with NULL (no-op).
     */
    void vessel_dispose(vessel_t handle);

#ifdef __cplusplus
}
#endif

#endif /* VESSEL_H */