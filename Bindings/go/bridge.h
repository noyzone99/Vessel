#ifndef VESSEL_GO_BRIDGE_H
#define VESSEL_GO_BRIDGE_H

#include <vessel.h>

/* Vessel's opaque handles are tokens. Keep them out of Go's pointer graph. */
vessel_error_t go_vessel_open(const char* path, int32_t metadata_compression, uintptr_t* out_handle);
void go_vessel_close(uintptr_t handle);
vessel_error_t go_vessel_put(uintptr_t handle, const char* key, uintptr_t reader, int32_t overwrite,
    vessel_object_info_t info, int32_t compression, int32_t level, vessel_metadata_result_t* out);
vessel_error_t go_vessel_metadata(uintptr_t handle, const char* key, vessel_metadata_result_t* out);
vessel_error_t go_vessel_keys(uintptr_t handle, const char* prefix, uintptr_t keys);
vessel_error_t go_vessel_delete(uintptr_t handle, const char* key, int32_t* deleted);

vessel_error_t go_vessel_stream_open(uintptr_t handle, const char* key, uintptr_t* out_stream);
vessel_error_t go_vessel_stream_read(uintptr_t stream, uint8_t* buffer, int32_t length, int32_t* read);
vessel_error_t go_vessel_stream_metadata(uintptr_t stream, vessel_metadata_result_t* out);
vessel_error_t go_vessel_stream_size(uintptr_t stream, int64_t* size);
vessel_error_t go_vessel_stream_can_seek(uintptr_t stream, int32_t* can_seek);
vessel_error_t go_vessel_stream_seek(uintptr_t stream, int64_t offset, int32_t whence, int64_t* position);
void go_vessel_stream_close(uintptr_t stream);

#endif
