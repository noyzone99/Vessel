/**
 * @file vessel.h
 * @brief Vessel Storage Native API - High-performance local blob storage.
 *
 * This header defines the C interface for the Vessel library, compiled via .NET NativeAOT.
 * All functions return a status code from the vessel_error_t enumeration.
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
     * @brief Error codes returned by Vessel API functions.
     */
    typedef enum {
        VESSEL_SUCCESS = 0,             /**< Operation completed successfully. */
        VESSEL_ERR_UNKNOWN = -1,        /**< An unexpected internal error occurred. */
        VESSEL_ERR_INVALID_ARGUMENT = -2,/**< One or more arguments are null or invalid. */
        VESSEL_ERR_NOT_FOUND = -3,       /**< The requested key was not found. */
        VESSEL_ERR_ALREADY_EXISTS = -4,  /**< Object exists and overwrite was not allowed. */
        VESSEL_ERR_IO = -5,              /**< A filesystem or I/O error occurred. */
        VESSEL_ERR_DISPOSED = -6,        /**< The storage instance has been disposed. */
        VESSEL_ERR_INVALID_OPERATION = -7 /**< Operation not allowed in current state. */
    } vessel_error_t;

    /**
     * @brief Input metadata for object storage operations.
     * Corresponds to ObjectInfo in C#.
     */
    typedef struct {
        const char* file_name;      /**< Optional: Original file name. Can be NULL. */
        const char* file_extension; /**< Optional: File extension. Can be NULL. */
        const char* mime_type;      /**< Optional: Content MIME type. Can be NULL. */
    } vessel_object_info_t;

    /**
     * @brief Container for retrieved object data and metadata.
     *
     * IMPORTANT: This structure contains pointers to memory allocated by the library.
     * You MUST call vessel_free_object() to release this memory once you are done.
     */
    typedef struct {
        uint8_t* data;          /**< Pointer to the payload buffer. */
        int64_t  length;        /**< Size of the data buffer in bytes. */
        char* checksum;      /**< SHA256 hash of the content. */
        char* file_name;     /**< Stored file name. Can be NULL. */
        char* extension;     /**< Stored file extension. Can be NULL. */
        char* mime_type;     /**< Stored MIME type. Can be NULL. */
    } vessel_object_t;

    /**
     * @brief Callback signature for key enumeration.
     * @param key Null-terminated UTF-8 string containing the object key.
     */
    typedef void (*vessel_key_callback)(const char* key);

    /**
     * @brief Initializes the storage engine at the specified path.
     *
     * @param path Root directory for storage.
     * @param out_handle Pointer to receive the instance handle.
     * @return vessel_error_t status.
     */
    vessel_error_t vessel_init(const char* path, vessel_t* out_handle);

    /**
     * @brief Stores an object payload.
     *
     * @param handle Storage handle.
     * @param key Unique identifier for the object.
     * @param data Pointer to the buffer to store.
     * @param length Size of the buffer.
     * @param overwrite 1 to replace existing, 0 to fail if exists.
     * @param info Optional metadata struct.
     * @param comp_type Compression algorithm (0=None, 1=Zstd, 2=LZ4).
     * @param comp_level Compression level (0=Fastest, 1=Balanced, 2=High, 3=Ultra).
     * @return vessel_error_t status.
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
     * @brief Reads an object (data + metadata) from storage.
     *
     * @param handle Storage handle.
     * @param key Key to look up.
     * @param out_obj Pointer to a vessel_object_t that will be filled with data.
     * @return vessel_error_t status.
     */
    vessel_error_t vessel_read(vessel_t handle, const char* key, vessel_object_t* out_obj);

    /**
     * @brief Deletes an object from storage.
     *
     * @param handle Storage handle.
     * @param key Key to delete.
     * @param out_deleted Pointer to receive 1 if deleted, 0 if not found.
     * @return vessel_error_t status.
     */
    vessel_error_t vessel_delete(vessel_t handle, const char* key, int32_t* out_deleted);

    /**
     * @brief Enumerates all keys matching the prefix.
     *
     * @param handle Storage handle.
     * @param prefix Search prefix (empty string for all keys).
     * @param callback Function called for every found key.
     * @return vessel_error_t status.
     */
    vessel_error_t vessel_get_all_keys(vessel_t handle, const char* prefix, vessel_key_callback callback);

    /**
     * @brief Safely releases all memory associated with a vessel_object_t.
     */
    void vessel_free_object(vessel_object_t obj);

    /**
     * @brief Disposes the storage engine and releases the handle.
     */
    void vessel_dispose(vessel_t handle);

#ifdef __cplusplus
}
#endif

#endif /* VESSEL_H */