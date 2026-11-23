# C++ API reference

Include [`<vessel/vessel.hpp>`](../../../Bindings/cpp/include/vessel/vessel.hpp) and link a matching [Vessel C ABI 1](../C/README.md) shared library. The source-distributed header requires C++17. See the [C++ user guide](../../UserGuide/Cpp/README.md) for setup.

## `vessel::Vessel`

| Operation | Result and behavior |
| --- | --- |
| `Vessel(path, options = {})` | Validates the linked ABI version and opens a store at the UTF-8 path. `Options::metadata_compression` defaults to `false`. |
| `put(key, source, options = {})` | Reads an `std::istream&` synchronously from its current position until EOF. If reading fails, Vessel aborts the upload and preserves the previous object. |
| `put(key, bytes, options = {})` | Writes the bytes in an `std::string_view`. Supply the explicit byte length for binary data containing NULs. |
| `open_read(key)` | Returns a move-only `Reader` for the current version. A missing key throws `Error` with `VESSEL_ERR_NOT_FOUND`. |
| `metadata(key)` | Returns an owned `Metadata` value for the current version without reading the payload. A missing key throws `Error` with `VESSEL_ERR_NOT_FOUND`. |
| `keys(prefix = {})` | Copies matching keys into a `std::vector<std::string>`. An empty prefix matches all keys. |
| `erase(key)` | Returns `true` when an object was deleted or `false` when the key was absent. |

`PutOptions` defaults to no overwrite, `Compression::none`, `CompressionLevel::balanced` and omitted file attributes. Set `overwrite` to `true` to replace an object. The `info` field accepts optional UTF-8 `file_name`, `extension` and `mime_type` strings. Keys, paths and attributes reject embedded NULs. A successful write returns `void`.

## `vessel::Reader`

| Operation | Result and behavior |
| --- | --- |
| `read(buffer, size)` | Reads into a caller-owned `char*` range and returns the byte count as `std::size_t`. Each call reads at most `INT32_MAX` bytes. With positive `size`, zero means EOF. |
| `copy_to(output)` | Copies bytes from the current position to an `std::ostream&` through a fixed-size buffer. If writing fails, the reader may have advanced. |
| `length()` | Returns the uncompressed length in bytes as `std::int64_t`. |
| `metadata()` | Returns owned metadata for the version captured when the reader opened. |
| `can_seek()` | Returns `true` for uncompressed objects. |
| `position()` | Returns the current byte position for seekable objects. |
| `seek(offset, origin = SeekOrigin::begin)` | Changes the read position. Origins are `begin`, `current` and `end`. |

Compressed streams report `VESSEL_ERR_INVALID_OPERATION` for `position` and `seek`. A reader stays usable after the store object is destroyed or the key is overwritten or deleted.

## Metadata, errors and ownership

`Metadata` includes `key`, `hash`, `etag`, `original_size`, `compressed_size`, `compression`, `creation_date_unix_ms` and `info`. The timestamp is UTC milliseconds since the Unix epoch. `Metadata` owns the strings, including optional file attributes.

Native failures throw `vessel::Error`. Call `status()` to get the [C ABI status code](../C/README.md#status-codes). Input and output failures are C++ exceptions, including `std::ios_base::failure` for a failed stream state. Allocation failures can throw `std::bad_alloc`.

`Vessel` and `Reader` release their native handles on destruction. They are move-only. Finish active calls before moving or destroying either object. Separate operations on a live `Vessel` may run concurrently subject to the [storage concurrency rules](../../UserGuide/Storage.md#open-stores-and-readers). Calls on the same `Reader` share a read position. Use separate readers for independent positions and keep each operation's input, output and buffers valid throughout the call.
