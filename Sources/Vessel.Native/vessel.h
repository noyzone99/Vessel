#ifndef VESSEL_H
#define VESSEL_H

#include <stdint.h>
#include <stddef.h>

/**
 * @brief Opaque handle representing a VesselStorage instance.
 */
typedef void* vessel_t;

/**
 * @brief Granular error codes for mapping managed exceptions to native callers.
 */
typedef enum {
    VESSEL_SUCCESS = 0,
    VESSEL_ERR_UNKNOWN = -1,
    VESSEL_ERR_INVALID_ARGUMENT = -2,
    VESSEL_ERR_NOT_FOUND = -3,
    VESSEL_ERR_ALREADY_EXISTS = -4,
    VESSEL_ERR_IO = -5,
    VESSEL_ERR_DISPOSED = -6,
    VESSEL_ERR_INVALID_OPERATION = -7
} vessel_error_t;

/**
 * @brief Input structure for providing optional object metadata during storage.
 * Maps to the managed 'ObjectInfo' record.
 */
typedef struct {
    const char* file_name;      /* Optional: Original file name (nullable) */
    const char* file_extension; /* Optional: Original file extension (nullable) */
    const char* mime_type;      /* Optional: Content MIME type (nullable) */
} vessel_object_info_t;

/**
 * @brief Output structure containing full object metadata.
 * Maps to the managed 'ObjectMetadata' record.
 *
 * NOTE: String fields (key, hash, etc.) are allocated by the library
 * and MUST be released by calling vessel_free_metadata().
 */
typedef struct {
    int64_t size;               /* Original uncompressed size in bytes */
    int64_t compressed_size;    /* Actual size stored on disk in bytes */
    int32_t compression_type;   /* Algorithm used (0=None, 1=Zstd, 2=LZ4) */
    int64_t last_modified_unix; /* Last modified timestamp (Unix Seconds) */
    char* key;                  /* The logical key identifier */
    char* hash;                 /* The content-based SHA256 hash */
    char* etag;                 /* Entity tag for cache validation */
    char* file_name;            /* Stored file name (nullable) */
    char* extension;            /* Stored file extension (nullable) */
    char* mime_type;            /* Stored MIME type (nullable) */
} vessel_metadata_t;

/**
 * @brief Callback signature for key enumeration.
 * Called once for every key matching the prefix in vessel_get_all_keys.
 */
typedef void (*vessel_key_callback)(const char* key);

#ifdef __cplusplus
extern "C" {
#endif

    /**
     * @brief Initializes a new VesselStorage instance at the specified path.
     *
     * @param path The root directory for metadata and blobs.
     * @param out_handle Pointer to receive the storage handle.
     * @return VESSEL_SUCCESS on success, or an error code.
     */
    vessel_error_t vessel_init(const char* path, vessel_t* out_handle);

    /**
     * @brief Stores an object payload.
     *
     * @param handle The storage instance handle.
     * @param key The stable logical identifier.
     * @param data Pointer to the buffer containing the payload.
     * @param length Length of the data buffer in bytes.
     * @param overwrite 1 to replace existing object, 0 to fail if key exists.
     * @param info Optional metadata (can be empty struct).
     * @param comp_type Compression algorithm (0=None, 1=Zstd, 2=LZ4).
     * @param comp_level Compression level (0=Fastest, 1=Balanced, 2=High, 3=Ultra).
     * @return VESSEL_SUCCESS on success, or an error code.
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
     * @brief Retrieves an object payload and its metadata.
     *
     * @param handle The storage instance handle.
     * @param key The logical key to look up.
     * @param out_data Pointer to receive the buffer address (Must be freed with vessel_free_buffer).
     * @param out_len Pointer to receive the length of the retrieved data.
     * @param out_metadata Pointer to receive the metadata (Must be freed with vessel_free_metadata).
     * @return VESSEL_SUCCESS on success, or VESSEL_ERR_NOT_FOUND.
     */
    vessel_error_t vessel_read(
        vessel_t handle,
        const char* key,
        uint8_t** out_data,
        int32_t* out_len,
        vessel_metadata_t* out_metadata);

    /**
     * @brief Deletes an object from the storage.
     *
     * @param handle The storage instance handle.
     * @param key The logical key to delete.
     * @param out_deleted Pointer to receive 1 if an object was actually deleted, 0 otherwise.
     * @return VESSEL_SUCCESS on success, or an error code.
     */
    vessel_error_t vessel_delete(vessel_t handle, const char* key, int32_t* out_deleted);

    /**
     * @brief Enumerates all keys matching the specified prefix.
     *
     * @param handle The storage instance handle.
     * @param prefix The string to match at the beginning of keys (empty string for all keys).
     * @param callback Function called for each key found.
     * @return VESSEL_SUCCESS on success, or an error code.
     */
    vessel_error_t vessel_get_all_keys(vessel_t handle, const char* prefix, vessel_key_callback callback);

    /**
     * @brief Frees the string members within a metadata structure.
     * Should be called after vessel_get or vessel_get_metadata.
     */
    void vessel_free_metadata(vessel_metadata_t metadata);

    /**
     * @brief Frees a data buffer allocated by the library.
     */
    void vessel_free_buffer(uint8_t* buffer);

    /**
     * @brief Disposes of the storage instance and releases the managed handle.
     */
    void vessel_dispose(vessel_t handle);

#ifdef __cplusplus
}
#endif

#endif /* VESSEL_H */