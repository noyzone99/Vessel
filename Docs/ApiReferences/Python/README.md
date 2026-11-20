# Python API reference

The `vessel-storage` package exports these types from `vessel`. It calls the [Vessel C ABI](../C/README.md) through the standard library's `ctypes` module and checks for ABI version 1 on load. Follow the [Python user guide](../../UserGuide/Python/README.md) for installation and examples.

## VesselStorage

`VesselStorage(path, *, library_path=None)` opens or creates a store on a local filesystem. Both paths accept strings or path-like objects returning strings. The wrapper tries `library_path`, then `VESSEL_LIBRARY_PATH`, then the shared library bundled in the installed wheel.

Use a context manager or call `close()`. Closing is idempotent, and `closed` reports whether the handle has been closed. Multiple threads can operate on one store. Use independent sources for concurrent uploads and wait for in-flight calls to finish before reopening the same directory. [Storage behavior](../../UserGuide/Storage.md)

| Method | Result and behavior |
| --- | --- |
| `put(key, data, *, overwrite=False, info=None, compression=CompressionType.NONE, level=CompressionLevel.BALANCED)` | Stores `bytes` and returns `ObjectMetadata`. Binary payloads may contain NUL bytes. |
| `put_stream(key, source, *, overwrite=False, info=None, compression=CompressionType.NONE, level=CompressionLevel.BALANCED)` | Returns `ObjectMetadata`. Calls `source.read(size)` from the current position through EOF. Each call must return up to `size` bytes, with `b""` marking EOF. The caller owns the source. A source exception cancels the upload, preserves the previous object and propagates to the caller. |
| `read(key)` | Returns the verified payload as `bytes`, up to 64 MiB. Use `open()` for larger objects. |
| `open(key)` | Returns a read-only `VesselStream` with a snapshot of the current payload and metadata. |
| `get_metadata(key)` | Returns `ObjectMetadata` without opening a payload stream. |
| `keys(prefix="")` | Returns a list of matching keys. An empty prefix selects all keys. |
| `delete(key)` | Returns `True` if an object was deleted or `False` if the key was absent. |

Keys, prefixes, store paths and file metadata are encoded as UTF-8. An embedded NUL in any of those strings raises `ValueError`.

## VesselStream

Get a `VesselStream` from `VesselStorage.open()`. It implements the read-only [`io.RawIOBase`](https://docs.python.org/3/library/io.html#io.RawIOBase) interface and works with `io.BufferedReader`. Use a context manager or call `close()` to release the handle.

- `length` gives the uncompressed length in bytes. `metadata` returns the stream's snapshot as `ObjectMetadata`.
- `read(size=-1)` returns up to `size` bytes. A negative size reads to EOF. `readinto(buffer)` reads into a writable, contiguous buffer and returns the number of bytes read.
- `seekable()` reports whether seeking is supported. On uncompressed objects, `seek(offset, whence=os.SEEK_SET)` returns the new position and `tell()` reports the current position. Offsets are signed 64-bit byte counts. Compressed streams raise `io.UnsupportedOperation` on `seek()` and `tell()`.
- Streams remain usable after overwrite, deletion or store closure. Use a separate stream per reader to keep read positions independent.

## Metadata and compression

| Type | Fields or values |
| --- | --- |
| `ObjectInfo(file_name=None, extension=None, mime_type=None)` | Optional file attributes for uploads. |
| `ObjectMetadata` | Immutable result with `key`, `hash`, `etag`, `original_size`, `compressed_size`, `compression_type`, `creation_date`, `file_name`, `extension` and `mime_type`. Sizes are in bytes. `creation_date` is a timezone-aware UTC `datetime` with millisecond precision. |
| `CompressionType` | `NONE`, `ZSTD`, `LZ4`. |
| `CompressionLevel` | `FASTEST`, `BALANCED`, `HIGH`, `ULTRA`. |

`ObjectMetadata.hash` is the physical blob identifier and can include a codec suffix. `ObjectMetadata.content_hash` is the SHA-256 digest of the uncompressed payload.

## Errors

| Exception | Meaning |
| --- | --- |
| `VesselLibraryError` | The native library path is missing, the library or a required export cannot be loaded, or the ABI version differs. |
| `ObjectNotFoundError` | The requested key does not exist. Subclass of `VesselError`. |
| `ObjectAlreadyExistsError` | The key already exists with overwrite disabled. Subclass of `VesselError`. |
| `VesselError` | A native operation failed. Its `status` is the [C ABI status code](../C/README.md#status-codes), including `-9` when `read()` exceeds the whole-read limit. |
| `ValueError` | An operation uses a closed store or stream, or a Python argument has an invalid value. |

Python results own their bytes and metadata strings. The wrapper releases temporary native allocations after copying the result. For direct ABI use, see [`vessel.h`](../../../Sources/Vessel.Native/vessel.h) and the [raw ctypes smoke test](../../../Tests/Vessel.Native.Tests/smoke.py).
