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
     * IMPORTANT: This structure contains allocated strings.
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
     * IMPORTANT: Contains allocated buffer and strings.
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
     * @param key A null-terminated UTF-8 string.
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
     * @brief Reads an object's payload and essential metadata.
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
     */
    vessel_error_t vessel_get_all_keys(vessel_t handle, const char* prefix, vessel_key_callback callback);

    /**
     * @brief Frees the payload buffer and strings inside a vessel_object_t.
     */
    void vessel_free_object(vessel_object_t obj);

    /**
     * @brief Frees all string members inside a vessel_metadata_t.
     */
    void vessel_free_metadata(vessel_metadata_t metadata);

    /**
     * @brief Closes the storage engine and releases the handle.
     */
    void vessel_dispose(vessel_t handle);

#ifdef __cplusplus
}
#endif

#endif /* VESSEL_H */