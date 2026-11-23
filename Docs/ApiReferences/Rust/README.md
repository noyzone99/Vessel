# Rust API reference

The source-distributed `vessel-storage` crate exports the `vessel` library. Its API requires Rust 1.85 or later and a matching [Vessel C ABI 1](../C/README.md) shared library. See the [Rust user guide](../../UserGuide/Rust/README.md) for setup.

## `vessel::Vessel`

| Operation | Result and behavior |
| --- | --- |
| `open(path)` | Opens or creates a store. Accepts an `AsRef<Path>` path that can be represented as UTF-8. |
| `open_with_options(path, options)` | Opens a store with `OpenOptions`. Metadata compression is disabled by default. |
| `put(key, input, options)` | Reads an `std::io::Read` source from its current position and returns committed `ObjectMetadata`. Pass `&mut input` to keep ownership of the source. If reading fails, Vessel cancels the write and preserves the previous object. |
| `open_read(key)` | Returns a `Reader` for the committed version. A missing key returns `Error::Native(-3)`. |
| `metadata(key)` | Returns owned `ObjectMetadata` without reading the payload. A missing key returns `Error::Native(-3)`. |
| `keys(prefix)` | Copies matching keys into a `Vec<String>`. An empty prefix matches all keys. |
| `delete(key)` | Returns `true` when an object was removed or `false` when the key was absent. |

`WriteOptions::default()` preserves existing keys, selects `Compression::None` and `CompressionLevel::Balanced` and omits the optional file attributes. Set `overwrite` to replace an object, `compression` to choose a codec and `level` to set its effort. The `info` field accepts an `ObjectInfo` with optional UTF-8 `file_name`, `extension` and `mime_type`. Keys, paths and attributes reject embedded NULs.

## `vessel::Reader`

| Operation | Result and behavior |
| --- | --- |
| `Read::read(&mut buf)` | Reads into the caller's initialized byte slice. Each call reads at most `i32::MAX` bytes. With a nonempty buffer, zero means EOF. |
| `Seek::seek(position)` | Changes the read position for an uncompressed stream. Seeking a compressed stream returns `std::io::ErrorKind::Unsupported`. |
| `size()` | Returns the uncompressed payload length in bytes as `u64`. |
| `metadata()` | Returns owned metadata for the version captured when this reader opened. |
| `can_seek()` | Reports whether the object supports seeking. |

`Vessel` and `Reader` release their native handles on drop. Independent operations on a live `Vessel` can run concurrently. Each `Reader` owns one position and remains readable after its `Vessel` is dropped or the key is replaced or deleted. Open separate readers for independent positions. [Shared storage behavior](../../UserGuide/Storage.md#open-stores-and-readers)

## Metadata and errors

`ObjectMetadata` contains the key, blob identifier, ETag, sizes, codec, `creation_date_unix_ms` in UTC and optional file attributes. Use `content_hash()` for the SHA-256 digest of the original payload without a codec suffix.

`Error::Native(status)` carries a [C ABI status code](../C/README.md#status-codes). `AbiVersion` identifies a mismatched native library, `InvalidText` identifies a string that cannot be passed to the C ABI, `InvalidNativeResult` indicates invalid data returned by the library, and `Input` wraps an upload source error. The `Read` and `Seek` traits report `std::io::Error`.
