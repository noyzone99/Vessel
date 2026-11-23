use std::ffi::{c_char, c_void};

pub const ABI_VERSION: i32 = 1;
pub const SUCCESS: i32 = 0;
pub const NOT_FOUND: i32 = -3;
pub const INVALID_OPERATION: i32 = -7;
pub const CANCELLED: i32 = -8;

pub type Handle = *mut c_void;
pub type ReadCallback = unsafe extern "C" fn(*mut c_void, *mut u8, i32) -> i32;
pub type KeyCallback = unsafe extern "C" fn(*mut c_void, *const c_char) -> i32;

#[repr(C)]
pub struct Options {
    pub enable_metadata_compression: i32,
    pub log: Option<unsafe extern "C" fn(*mut c_void, i32, *const c_char)>,
    pub log_context: *mut c_void,
}

#[repr(C)]
pub struct ObjectInfo {
    pub file_name: *const c_char,
    pub extension: *const c_char,
    pub mime_type: *const c_char,
}

#[repr(C)]
pub struct Metadata {
    pub original_size: i64,
    pub compressed_size: i64,
    pub compression_type: i32,
    pub creation_date_unix: i64,
    pub key: *mut c_char,
    pub hash: *mut c_char,
    pub etag: *mut c_char,
    pub file_name: *mut c_char,
    pub extension: *mut c_char,
    pub mime_type: *mut c_char,
}

#[repr(C)]
pub struct MetadataResult {
    pub metadata: Metadata,
    pub creation_date_unix_ms: i64,
}

unsafe extern "C" {
    pub fn vessel_abi_version() -> i32;
    pub fn vessel_init_ex(path: *const c_char, options: *const Options, out: *mut Handle) -> i32;
    pub fn vessel_dispose(handle: Handle);
    pub fn vessel_put_stream(
        handle: Handle,
        key: *const c_char,
        read: ReadCallback,
        context: *mut c_void,
        overwrite: i32,
        info: ObjectInfo,
        compression: i32,
        level: i32,
        out: *mut MetadataResult,
    ) -> i32;
    pub fn vessel_get_metadata_ex(
        handle: Handle,
        key: *const c_char,
        out: *mut MetadataResult,
    ) -> i32;
    pub fn vessel_get_all_keys_ex(
        handle: Handle,
        prefix: *const c_char,
        callback: KeyCallback,
        context: *mut c_void,
    ) -> i32;
    pub fn vessel_delete(handle: Handle, key: *const c_char, deleted: *mut i32) -> i32;
    pub fn vessel_stream_open(handle: Handle, key: *const c_char, out: *mut Handle) -> i32;
    pub fn vessel_stream_read(handle: Handle, buffer: *mut u8, count: i32, out: *mut i32) -> i32;
    pub fn vessel_stream_length(handle: Handle, out: *mut i64) -> i32;
    pub fn vessel_stream_get_metadata(handle: Handle, out: *mut MetadataResult) -> i32;
    pub fn vessel_stream_can_seek(handle: Handle, out: *mut i32) -> i32;
    pub fn vessel_stream_seek(handle: Handle, offset: i64, origin: i32) -> i32;
    pub fn vessel_stream_position(handle: Handle, out: *mut i64) -> i32;
    pub fn vessel_stream_close(handle: Handle);
    pub fn vessel_free_metadata(metadata: *mut Metadata);
}
