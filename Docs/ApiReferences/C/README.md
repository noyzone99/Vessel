# C ABI reference

[`vessel.h`](../../../Sources/Vessel.Native/vessel.h) defines the signatures, field order and constant values for dynamic and static linking. Use the header and library from the same native archive. Check `vessel_abi_version()` against `VESSEL_ABI_VERSION`, currently `1`. See the [C user guide](../../UserGuide/C/README.md) for release downloads and linking examples.

## Types and calling convention

- Functions and callbacks use the C calling convention (`VESSEL_CALL`, `__cdecl` on Windows). Calls are synchronous except for push upload completion. A language binding can call Vessel from its runtime's worker threads.
- `vessel_t`, `vessel_stream_t` and `vessel_upload_t` are pointer-sized opaque handles. FFI bindings must preserve the header's native struct alignment and signed `int32_t` and `int64_t` widths.
- Text is NUL-terminated UTF-8. `vessel_object_info_t` carries optional filename, extension and MIME type pointers. A `NULL` field represents an omitted value.
- `vessel_metadata_t` includes the key, blob identifier, ETag, sizes, codec, file attributes and UTC timestamp in Unix seconds. `vessel_metadata_result_t` embeds it and adds the UTC timestamp in Unix milliseconds.
- `vessel_object_t` carries a payload pointer, its byte length, the original payload's SHA-256 checksum and optional file attributes. Use the length to copy binary data.

## Store and object operations

| Function | Result and behavior |
| --- | --- |
| `vessel_abi_version` | Returns the ABI version as `int32_t`. |
| `vessel_init`, `vessel_init_ex` | Open a directory and write a store handle to the output. `_ex` accepts `vessel_options_t` for metadata compression (`0` or `1`) and a logging callback. Absent options disable both. |
| `vessel_put`, `vessel_put_ex` | Write a byte buffer to a key. `vessel_put` takes a signed 32-bit length. `_ex` accepts a signed 64-bit length and an optional metadata output. A zero length allows a `NULL` payload. |
| `vessel_put_stream` | Pulls payload bytes from a callback and accepts an optional metadata output. A cancelled upload preserves the previous object. |
| `vessel_read` | Returns the whole object up to `VESSEL_WHOLE_READ_LIMIT` (64 MiB) and checks the uncompressed length and checksum. Use streaming for larger objects. |
| `vessel_get_metadata`, `vessel_get_metadata_ex` | Return metadata without reading the payload. `_ex` also returns the millisecond timestamp. A missing key returns `VESSEL_ERR_NOT_FOUND`. |
| `vessel_get_all_keys`, `vessel_get_all_keys_ex` | Pass matching keys to a callback. A `NULL` or empty prefix matches all keys. The `_ex` callback receives a context and can stop enumeration. |
| `vessel_delete` | Returns success with `out_deleted` set to `1` when an object was removed or `0` when the key was absent. |
| `vessel_dispose` | Closes the store handle. Repeated closes are safe. |

For all writes, pass `0` to reject an existing key or a nonzero `overwrite` value to replace it. Select `VESSEL_COMPRESSION_NONE`, `VESSEL_COMPRESSION_ZSTD` or `VESSEL_COMPRESSION_LZ4` and one of `VESSEL_LEVEL_FASTEST`, `VESSEL_LEVEL_BALANCED`, `VESSEL_LEVEL_HIGH` or `VESSEL_LEVEL_ULTRA`.

When a caller requests write metadata, Vessel allocates the result storage before publication. If that allocation fails, the write returns `VESSEL_ERR_OUT_OF_MEMORY` before publishing.

## Stream operations

| Function | Result and behavior |
| --- | --- |
| `vessel_stream_open` | Opens a read-only stream for the current object version. The stream can outlive the store handle and stays readable after an overwrite or deletion. |
| `vessel_stream_read` | Reads up to `count` bytes into caller-owned storage. With a positive `count`, zero bytes read signals end of stream. |
| `vessel_stream_length` | Returns the uncompressed payload length in bytes. |
| `vessel_stream_get_metadata` | Returns extended metadata for the opened version. |
| `vessel_stream_can_seek` | Returns `1` for uncompressed streams and `0` for compressed streams. |
| `vessel_stream_position` | Returns the read position for a seekable stream. |
| `vessel_stream_seek` | Changes the read position using `VESSEL_SEEK_SET`, `VESSEL_SEEK_CUR` or `VESSEL_SEEK_END`. Compressed streams return `VESSEL_ERR_INVALID_OPERATION` for position and seek. |
| `vessel_stream_close` | Closes the stream handle. Repeated closes are safe. |

Vessel serializes calls on an individual native stream. Open a separate handle for each independent read position.

## Push upload operations

| Function | Result and behavior |
| --- | --- |
| `vessel_upload_begin` | Allocates result storage and starts a write to a key, returning an upload handle. On success the completion callback runs once, potentially before begin returns. The callback receives the terminal status and borrowed metadata on success. A failed begin leaves the handle zero and never calls the callback. |
| `vessel_upload_write` | Copies up to 64 KiB from caller-owned memory. A nonzero `out_paused` indicates backpressure. |
| `vessel_upload_drain` | Waits for that backpressure to clear. Call it when write reports a pause, before writing again or committing. |
| `vessel_upload_end` | Releases the upload handle. A nonzero `commit` signals EOF and requests publication. Zero cancels the write and can interrupt a pending drain. Call end even if completion already reported an error. Repeated calls are safe. |

Use the completion status to determine the write outcome. A failed drain or write can occur after the consumer has stopped and the callback carries the underlying failure. Serialize producer calls for each upload. Keep the completion callback and its context alive until it returns, and consume any borrowed metadata inside the callback. Completion can run on a library thread. Commit and abort are terminal and the first end request wins. Keep independent upload handles for concurrent writes.

## Callbacks and resource ownership

- The `vessel_put_stream` input callback runs on the calling thread. Write at most `capacity` bytes to the borrowed buffer and return the count, `0` at EOF, `VESSEL_ERR_CANCELLED` to cancel or another negative value to signal I/O failure.
- Enumeration callbacks run on the calling thread. The extended callback returns `0` to continue, a positive number to stop successfully or a negative number to cancel. Callback text is borrowed until the callback returns.
- Logging can run on library threads. Make the callback and context safe for concurrent use. Keep callback objects and contexts alive until the associated operations finish, even if the handle has already been closed.
- Pass writable, zero-initialized output storage. Free each owned result before reusing it. Call `vessel_free_object` on whole-read results and `vessel_free_metadata` on metadata results. For `vessel_metadata_result_t`, pass `&result.metadata`. These functions accept `NULL` and clear the owned fields they free.
- Close stream and store handles with their matching functions. In-flight calls may complete after close. Wait for them before releasing their callback resources or reopening the directory.

## Status codes

| Status | Value | Meaning |
| --- | ---: | --- |
| `VESSEL_SUCCESS` | `0` | Operation completed successfully. |
| `VESSEL_ERR_UNKNOWN` | `-1` | Unexpected failure. |
| `VESSEL_ERR_INVALID_ARGUMENT` | `-2` | Invalid argument, such as a required null pointer, unsupported enum value or invalid key. |
| `VESSEL_ERR_NOT_FOUND` | `-3` | Requested object is missing. |
| `VESSEL_ERR_ALREADY_EXISTS` | `-4` | The key exists and overwrite is disabled. |
| `VESSEL_ERR_IO` | `-5` | Storage, permission, data integrity or callback I/O failure. |
| `VESSEL_ERR_DISPOSED` | `-6` | A nonzero handle no longer refers to an open store or stream. |
| `VESSEL_ERR_INVALID_OPERATION` | `-7` | The current operation is unsupported, such as seeking in a compressed stream. |
| `VESSEL_ERR_CANCELLED` | `-8` | A callback cancelled the operation. |
| `VESSEL_ERR_LIMIT_EXCEEDED` | `-9` | The whole-object read exceeds 64 MiB. |
| `VESSEL_ERR_OUT_OF_MEMORY` | `-10` | Memory allocation failed. |
