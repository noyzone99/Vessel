//! Streaming access to a local Vessel object store.

mod ffi;

use std::any::Any;
use std::ffi::{CStr, CString, c_char, c_void};
use std::fmt;
use std::io::{self, Read, Seek, SeekFrom};
use std::panic::{AssertUnwindSafe, catch_unwind, resume_unwind};
use std::path::Path;
use std::ptr::{self, NonNull};

#[derive(Debug)]
pub enum Error {
    Native(i32),
    AbiVersion(i32),
    InvalidText(&'static str),
    InvalidNativeResult,
    Input(io::Error),
}

impl fmt::Display for Error {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Native(status) => write!(formatter, "Vessel returned status {status}"),
            Self::AbiVersion(version) => write!(
                formatter,
                "Vessel C ABI {} required, found {version}",
                ffi::ABI_VERSION
            ),
            Self::InvalidText(field) => write!(formatter, "{field} must be UTF-8 without NUL"),
            Self::InvalidNativeResult => formatter.write_str("Vessel returned an invalid result"),
            Self::Input(error) => write!(formatter, "read upload input: {error}"),
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Input(error) => Some(error),
            _ => None,
        }
    }
}

impl From<Error> for io::Error {
    fn from(error: Error) -> Self {
        let kind = match error {
            Error::Native(ffi::NOT_FOUND) => io::ErrorKind::NotFound,
            Error::Native(ffi::INVALID_OPERATION) => io::ErrorKind::Unsupported,
            Error::InvalidText(_) => io::ErrorKind::InvalidInput,
            Error::InvalidNativeResult => io::ErrorKind::InvalidData,
            Error::Input(error) => return error,
            _ => io::ErrorKind::Other,
        };
        Self::new(kind, error)
    }
}

pub type Result<T> = std::result::Result<T, Error>;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(i32)]
pub enum Compression {
    #[default]
    None = 0,
    Zstd = 1,
    Lz4 = 2,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
#[repr(i32)]
pub enum CompressionLevel {
    Fastest = 0,
    #[default]
    Balanced = 1,
    High = 2,
    Ultra = 3,
}

#[derive(Clone, Copy, Debug, Default)]
pub struct OpenOptions {
    pub enable_metadata_compression: bool,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ObjectInfo {
    pub file_name: Option<String>,
    pub extension: Option<String>,
    pub mime_type: Option<String>,
}

#[derive(Clone, Debug, Default)]
pub struct WriteOptions {
    pub overwrite: bool,
    pub compression: Compression,
    pub level: CompressionLevel,
    pub info: ObjectInfo,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ObjectMetadata {
    pub key: String,
    pub hash: String,
    pub etag: String,
    pub original_size: i64,
    pub compressed_size: i64,
    pub compression: Compression,
    pub creation_date_unix_ms: i64,
    pub info: ObjectInfo,
}

impl ObjectMetadata {
    pub fn content_hash(&self) -> &str {
        self.hash
            .split_once('-')
            .map_or(&self.hash, |(digest, _)| digest)
    }
}

/// Owns an open store and releases it on drop.
pub struct Vessel(NonNull<c_void>);

// The C ABI permits concurrent store operations. The handle is an opaque token.
unsafe impl Send for Vessel {}
unsafe impl Sync for Vessel {}

impl Vessel {
    pub fn open(path: impl AsRef<Path>) -> Result<Self> {
        Self::open_with_options(path, OpenOptions::default())
    }

    pub fn open_with_options(path: impl AsRef<Path>, options: OpenOptions) -> Result<Self> {
        let version = unsafe { ffi::vessel_abi_version() };
        if version != ffi::ABI_VERSION {
            return Err(Error::AbiVersion(version));
        }
        let path = path
            .as_ref()
            .to_str()
            .ok_or(Error::InvalidText("store path"))?;
        let path = text(path, "store path")?;
        let options = ffi::Options {
            enable_metadata_compression: i32::from(options.enable_metadata_compression),
            log: None,
            log_context: ptr::null_mut(),
        };
        let mut handle = ptr::null_mut();
        check(unsafe { ffi::vessel_init_ex(path.as_ptr(), &options, &mut handle) })?;
        Ok(Self(
            NonNull::new(handle).ok_or(Error::InvalidNativeResult)?,
        ))
    }

    /// Streams bytes from the current input position and returns committed metadata.
    pub fn put<R: Read>(
        &self,
        key: &str,
        input: R,
        options: &WriteOptions,
    ) -> Result<ObjectMetadata> {
        let key = text(key, "key")?;
        let file_name = optional_text(options.info.file_name.as_deref(), "file name")?;
        let extension = optional_text(options.info.extension.as_deref(), "extension")?;
        let mime_type = optional_text(options.info.mime_type.as_deref(), "MIME type")?;
        let info = ffi::ObjectInfo {
            file_name: text_ptr(&file_name),
            extension: text_ptr(&extension),
            mime_type: text_ptr(&mime_type),
        };
        let mut input = Upload {
            input,
            error: None,
            panic: None,
        };
        let mut metadata = NativeMetadata::new();
        // The native callback is synchronous and does not retain the stack context.
        let status = unsafe {
            ffi::vessel_put_stream(
                self.0.as_ptr(),
                key.as_ptr(),
                read_upload::<R>,
                ptr::from_mut(&mut input).cast(),
                i32::from(options.overwrite),
                info,
                options.compression as i32,
                options.level as i32,
                &mut metadata.0,
            )
        };
        if let Some(panic) = input.panic {
            resume_unwind(panic);
        }
        if let Some(error) = input.error {
            return Err(Error::Input(error));
        }
        check(status)?;
        metadata.copy()
    }

    pub fn open_read(&self, key: &str) -> Result<Reader> {
        let key = text(key, "key")?;
        let mut stream = ptr::null_mut();
        check(unsafe { ffi::vessel_stream_open(self.0.as_ptr(), key.as_ptr(), &mut stream) })?;
        Ok(Reader(
            NonNull::new(stream).ok_or(Error::InvalidNativeResult)?,
        ))
    }

    pub fn metadata(&self, key: &str) -> Result<ObjectMetadata> {
        let key = text(key, "key")?;
        let mut metadata = NativeMetadata::new();
        check(unsafe {
            ffi::vessel_get_metadata_ex(self.0.as_ptr(), key.as_ptr(), &mut metadata.0)
        })?;
        metadata.copy()
    }

    pub fn keys(&self, prefix: &str) -> Result<Vec<String>> {
        let prefix = text(prefix, "prefix")?;
        let mut keys = KeyList::default();
        // The native callback borrows the key and context until it returns.
        let status = unsafe {
            ffi::vessel_get_all_keys_ex(
                self.0.as_ptr(),
                prefix.as_ptr(),
                collect_key,
                ptr::from_mut(&mut keys).cast(),
            )
        };
        if keys.invalid {
            return Err(Error::InvalidNativeResult);
        }
        check(status)?;
        Ok(keys.values)
    }

    pub fn delete(&self, key: &str) -> Result<bool> {
        let key = text(key, "key")?;
        let mut deleted = 0;
        check(unsafe { ffi::vessel_delete(self.0.as_ptr(), key.as_ptr(), &mut deleted) })?;
        Ok(deleted != 0)
    }
}

impl Drop for Vessel {
    fn drop(&mut self) {
        unsafe { ffi::vessel_dispose(self.0.as_ptr()) }
    }
}

/// Owns an open snapshot. Dropping the store leaves the snapshot readable.
pub struct Reader(NonNull<c_void>);

// A reader's native operations serialize internally. Read and seek also borrow it mutably.
unsafe impl Send for Reader {}
unsafe impl Sync for Reader {}

impl Reader {
    pub fn size(&self) -> Result<u64> {
        let mut size = 0;
        check(unsafe { ffi::vessel_stream_length(self.0.as_ptr(), &mut size) })?;
        u64::try_from(size).map_err(|_| Error::InvalidNativeResult)
    }

    pub fn metadata(&self) -> Result<ObjectMetadata> {
        let mut metadata = NativeMetadata::new();
        check(unsafe { ffi::vessel_stream_get_metadata(self.0.as_ptr(), &mut metadata.0) })?;
        metadata.copy()
    }

    pub fn can_seek(&self) -> Result<bool> {
        let mut can_seek = 0;
        check(unsafe { ffi::vessel_stream_can_seek(self.0.as_ptr(), &mut can_seek) })?;
        Ok(can_seek != 0)
    }
}

impl Read for Reader {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        if buffer.is_empty() {
            return Ok(0);
        }
        let capacity = buffer.len().min(i32::MAX as usize);
        let mut count = 0;
        check(unsafe {
            ffi::vessel_stream_read(
                self.0.as_ptr(),
                buffer.as_mut_ptr(),
                capacity as i32,
                &mut count,
            )
        })?;
        match usize::try_from(count) {
            Ok(count) if count <= capacity => Ok(count),
            _ => Err(Error::InvalidNativeResult.into()),
        }
    }
}

impl Seek for Reader {
    fn seek(&mut self, position: SeekFrom) -> io::Result<u64> {
        let (offset, origin) = match position {
            SeekFrom::Start(offset) => (
                i64::try_from(offset).map_err(|_| io::Error::from(io::ErrorKind::InvalidInput))?,
                0,
            ),
            SeekFrom::Current(offset) => (offset, 1),
            SeekFrom::End(offset) => (offset, 2),
        };
        check(unsafe { ffi::vessel_stream_seek(self.0.as_ptr(), offset, origin) })?;
        self.stream_position()
    }

    fn stream_position(&mut self) -> io::Result<u64> {
        let mut position = 0;
        check(unsafe { ffi::vessel_stream_position(self.0.as_ptr(), &mut position) })?;
        u64::try_from(position).map_err(|_| Error::InvalidNativeResult.into())
    }
}

impl Drop for Reader {
    fn drop(&mut self) {
        unsafe { ffi::vessel_stream_close(self.0.as_ptr()) }
    }
}

fn check(status: i32) -> Result<()> {
    if status == ffi::SUCCESS {
        Ok(())
    } else {
        Err(Error::Native(status))
    }
}

fn text(value: &str, field: &'static str) -> Result<CString> {
    CString::new(value).map_err(|_| Error::InvalidText(field))
}

fn optional_text(value: Option<&str>, field: &'static str) -> Result<Option<CString>> {
    value.map(|value| text(value, field)).transpose()
}

fn text_ptr(value: &Option<CString>) -> *const c_char {
    value.as_ref().map_or(ptr::null(), |value| value.as_ptr())
}

// Copy the native UTF-8 string before its callback or metadata result releases it.
unsafe fn copy_text(value: *const c_char) -> Result<Option<String>> {
    if value.is_null() {
        return Ok(None);
    }
    unsafe { CStr::from_ptr(value) }
        .to_str()
        .map(|value| Some(value.to_owned()))
        .map_err(|_| Error::InvalidNativeResult)
}

struct NativeMetadata(ffi::MetadataResult);

impl NativeMetadata {
    fn new() -> Self {
        // This C layout contains only integers and raw pointers, which accept zero values.
        Self(unsafe { std::mem::zeroed() })
    }

    fn copy(&self) -> Result<ObjectMetadata> {
        let metadata = &self.0.metadata;
        let compression = match metadata.compression_type {
            0 => Compression::None,
            1 => Compression::Zstd,
            2 => Compression::Lz4,
            _ => return Err(Error::InvalidNativeResult),
        };
        unsafe {
            Ok(ObjectMetadata {
                key: copy_text(metadata.key)?.ok_or(Error::InvalidNativeResult)?,
                hash: copy_text(metadata.hash)?.ok_or(Error::InvalidNativeResult)?,
                etag: copy_text(metadata.etag)?.ok_or(Error::InvalidNativeResult)?,
                original_size: metadata.original_size,
                compressed_size: metadata.compressed_size,
                compression,
                creation_date_unix_ms: self.0.creation_date_unix_ms,
                info: ObjectInfo {
                    file_name: copy_text(metadata.file_name)?,
                    extension: copy_text(metadata.extension)?,
                    mime_type: copy_text(metadata.mime_type)?,
                },
            })
        }
    }
}

impl Drop for NativeMetadata {
    fn drop(&mut self) {
        unsafe { ffi::vessel_free_metadata(&mut self.0.metadata) }
    }
}

struct Upload<R> {
    input: R,
    error: Option<io::Error>,
    panic: Option<Box<dyn Any + Send>>,
}

unsafe extern "C" fn read_upload<R: Read>(
    context: *mut c_void,
    buffer: *mut u8,
    capacity: i32,
) -> i32 {
    let upload = unsafe { &mut *context.cast::<Upload<R>>() };
    let capacity = capacity as usize;
    let result = catch_unwind(AssertUnwindSafe(|| {
        // Read implementations may inspect the supplied bytes. Initialize the native buffer first.
        unsafe { buffer.write_bytes(0, capacity) };
        let buffer = unsafe { std::slice::from_raw_parts_mut(buffer, capacity) };
        loop {
            match upload.input.read(buffer) {
                Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
                Ok(count) if count <= capacity => return Ok(count as i32),
                Ok(_) => {
                    return Err(io::Error::new(
                        io::ErrorKind::InvalidData,
                        "input returned too many bytes",
                    ));
                }
                Err(error) => return Err(error),
            }
        }
    }));
    match result {
        Ok(Ok(count)) => count,
        Ok(Err(error)) => {
            upload.error = Some(error);
            ffi::CANCELLED
        }
        Err(panic) => {
            upload.panic = Some(panic);
            ffi::CANCELLED
        }
    }
}

#[derive(Default)]
struct KeyList {
    values: Vec<String>,
    invalid: bool,
}

unsafe extern "C" fn collect_key(context: *mut c_void, key: *const c_char) -> i32 {
    let keys = unsafe { &mut *context.cast::<KeyList>() };
    if let Ok(Some(key)) = unsafe { copy_text(key) } {
        keys.values.push(key);
        return 0;
    }
    keys.invalid = true;
    ffi::CANCELLED
}
