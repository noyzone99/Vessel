#include "bridge.h"
#include "_cgo_export.h"

static int32_t VESSEL_CALL read_input(void* context, uint8_t* buffer, int32_t capacity)
{
    return vesselGoRead(*(uintptr_t*)context, buffer, capacity);
}

static int32_t VESSEL_CALL add_key(void* context, const char* key)
{
    return vesselGoAddKey(*(uintptr_t*)context, (char*)key);
}

vessel_error_t go_vessel_open(const char* path, int32_t metadata_compression, uintptr_t* out_handle)
{
    vessel_options_t options = {0};
    options.enable_metadata_compression = metadata_compression;
    vessel_t handle = NULL;
    vessel_error_t status = vessel_init_ex(path, &options, &handle);
    *out_handle = (uintptr_t)handle;
    return status;
}

void go_vessel_close(uintptr_t handle)
{
    vessel_dispose((vessel_t)handle);
}

vessel_error_t go_vessel_put(uintptr_t handle, const char* key, uintptr_t reader, int32_t overwrite,
    vessel_object_info_t info, int32_t compression, int32_t level, vessel_metadata_result_t* out)
{
    return vessel_put_stream((vessel_t)handle, key, read_input, &reader, overwrite, info, compression, level, out);
}

vessel_error_t go_vessel_metadata(uintptr_t handle, const char* key, vessel_metadata_result_t* out)
{
    return vessel_get_metadata_ex((vessel_t)handle, key, out);
}

vessel_error_t go_vessel_keys(uintptr_t handle, const char* prefix, uintptr_t keys)
{
    return vessel_get_all_keys_ex((vessel_t)handle, prefix, add_key, &keys);
}

vessel_error_t go_vessel_delete(uintptr_t handle, const char* key, int32_t* deleted)
{
    return vessel_delete((vessel_t)handle, key, deleted);
}

vessel_error_t go_vessel_stream_open(uintptr_t handle, const char* key, uintptr_t* out_stream)
{
    vessel_stream_t stream = NULL;
    vessel_error_t status = vessel_stream_open((vessel_t)handle, key, &stream);
    *out_stream = (uintptr_t)stream;
    return status;
}

vessel_error_t go_vessel_stream_read(uintptr_t stream, uint8_t* buffer, int32_t length, int32_t* read)
{
    return vessel_stream_read((vessel_stream_t)stream, buffer, length, read);
}

vessel_error_t go_vessel_stream_metadata(uintptr_t stream, vessel_metadata_result_t* out)
{
    return vessel_stream_get_metadata((vessel_stream_t)stream, out);
}

vessel_error_t go_vessel_stream_size(uintptr_t stream, int64_t* size)
{
    return vessel_stream_length((vessel_stream_t)stream, size);
}

vessel_error_t go_vessel_stream_can_seek(uintptr_t stream, int32_t* can_seek)
{
    return vessel_stream_can_seek((vessel_stream_t)stream, can_seek);
}

vessel_error_t go_vessel_stream_seek(uintptr_t stream, int64_t offset, int32_t whence, int64_t* position)
{
    vessel_error_t status = vessel_stream_seek((vessel_stream_t)stream, offset, whence);
    return status == VESSEL_SUCCESS ? vessel_stream_position((vessel_stream_t)stream, position) : status;
}

void go_vessel_stream_close(uintptr_t stream)
{
    vessel_stream_close((vessel_stream_t)stream);
}
