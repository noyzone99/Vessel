# Go API reference

The module `github.com/noyzone99/Vessel/Bindings/go` exports package `vessel`. It uses Go 1.24 or later, cgo and a matching [Vessel C ABI 1](../C/README.md) shared library. See the [Go user guide](../../UserGuide/Go/README.md) for toolchain and linking instructions.

## `Vessel`

| Operation | Result and behavior |
| --- | --- |
| `Open(directory, options)` | Opens or creates a store. Pass `nil` for default options. `Options.EnableMetadataCompression` defaults to `false`. |
| `Put(key, input, options)` | Reads an `io.Reader` to EOF and returns committed `ObjectMetadata`. The caller owns the source. If reading fails, Vessel cancels the write and returns the input error. |
| `Open(key)` | Returns a `*Stream` for the committed version. A missing key returns `ErrNotFound`. |
| `Metadata(key)` | Reads metadata without opening the payload. A missing key returns `ErrNotFound`. |
| `Keys(prefix)` | Returns the matching keys as a slice. An empty prefix matches all keys. |
| `Delete(key)` | Returns whether it removed a key. An absent key returns `false` with a nil error. |
| `Close()` | Waits for active store operations and releases the store. It is safe to call again. |

`WriteOptions` defaults to preserving existing keys, no payload compression, a balanced compression level and omitted file attributes. Set `Overwrite` to replace an object. Keys, paths and attributes must be valid UTF-8 without NUL.

Independent operations on an open store can run concurrently. Keep the upload reader available throughout `Put`. Calling the same store from the reader can deadlock with a concurrent `Close`.

## `Stream`

| Operation | Result and behavior |
| --- | --- |
| `Read(p)` | Reads into the caller's byte slice and returns `io.EOF` at the end. |
| `Size()` and `Metadata()` | Return the uncompressed size and metadata for this stream's version. |
| `CanSeek()` and `Seek(offset, whence)` | Uncompressed streams support seeking. Compressed streams report `ErrInvalidOperation` when asked to seek. |
| `Close()` | Releases the read handle. It is safe to call again. |

The caller owns each stream. A stream stays readable after the store closes or the key is replaced or deleted. Operations on one stream share a read position. Open independent streams for readers that need separate positions. Keep the store and stream values behind their pointers.

## Metadata and errors

`ObjectMetadata` includes sizes, compression, UTC creation time, file attributes and the object key, ETag and blob identifier. Use `ContentHash()` for the payload SHA-256 without a codec suffix. Empty file attributes indicate absent values.

`Status` implements `error` and exposes the [C ABI status codes](../C/README.md#status-codes), including `ErrNotFound`, `ErrAlreadyExists` and `ErrDisposed`. Use `errors.Is` to compare against a status. An operation on a closed store or stream returns `ErrDisposed`.
