# Java API reference

The JDK 25 binding exports package `io.github.noyzone99.vessel` and uses [Vessel C ABI 1](../C/README.md). See the [Java user guide](../../UserGuide/Java/README.md) for the Maven and Gradle setup and JVM native access options.

See the [Kotlin/JVM reference](../Kotlin/README.md) for using the same API from Kotlin.

## `VesselStorage`

| Operation | Contract |
| --- | --- |
| `new VesselStorage(Path directory[, boolean enableMetadataCompression])` | Opens or creates a local store. Metadata compression defaults to `false`. |
| `put(String key, byte[] data[, WriteOptions options])` | Stores the bytes and returns the committed `ObjectMetadata`. The caller owns the array. |
| `put(String key, InputStream input[, WriteOptions options])` | Reads from the current input position to EOF with bounded buffering. The caller owns and closes the input. If reading fails, Vessel cancels the write and rethrows the input exception. An interrupted calling thread cancels the write when Vessel next requests input. |
| `read(String key)` | Returns a new byte array after verifying the payload. The limit is 64 MiB of uncompressed data. Use `open` for larger objects. |
| `open(String key)` | Returns an independently owned `VesselInputStream` over the version selected when the stream opened. |
| `metadata(String key)` | Returns metadata without reading the payload. |
| `keys([String prefix])` | Returns an immutable list of visible keys with the given prefix. The default empty prefix includes all keys. |
| `delete(String key)` | Returns `true` when the key was removed and `false` when the key was absent. |
| `close()` | Waits for active store operations and releases the store. Calling it again is safe. Close outside the source stream's upload callback. |

Store operations can run concurrently. Allow `close()` to finish before reopening the same directory. Keys, paths and metadata strings use UTF-8 and must be well-formed Unicode without NUL characters.

## `VesselInputStream`

This read-only `InputStream` supports ordinary Java streaming operations. It keeps its own read position and metadata snapshot across overwrites, deletions and store closure. Close each stream explicitly, preferably with try-with-resources. Stream operations are serialized per handle. Open a separate stream per independent reader.

- `length()` returns the uncompressed length in bytes.
- `metadata()` returns the snapshot's metadata.
- `canSeek()` is true for uncompressed payloads. `position()` returns a byte offset and `seek(long position)` sets an absolute byte offset on seekable streams. Both report `VESSEL_ERR_INVALID_OPERATION` on compressed streams.
- `close()` releases the stream and its native read buffer. Calling it again is safe.

## `WriteOptions` and `ObjectMetadata`

`WriteOptions` controls overwrite behavior, codec, compression level and the optional `fileName`, `extension` and `mimeType` strings. Defaults preserve an existing key, use `CompressionType.NONE` and `CompressionLevel.BALANCED`, and leave the file strings unset. The codecs are `NONE`, `ZSTD` and `LZ4`. Levels are `FASTEST`, `BALANCED`, `HIGH` and `ULTRA`.

`ObjectMetadata` contains the key, blob identifier `hash`, ETag, original and stored sizes in bytes, codec, creation timestamp as a millisecond-precision `Instant` and optional file strings. Use `contentHash()` for the SHA-256 digest of the original payload. Optional strings may be `null`.

## Errors and native loading

Native operation failures throw `VesselException`, a checked `IOException` with a `status()` matching the [C status table](../C/README.md#status-codes). Common codes include `-3` for an absent key, `-4` for a key conflict and `-9` for a whole-object read above the limit. Operations on closed Java handles throw `IOException`. An interrupted upload throws `InterruptedIOException` and preserves any previous object.

Native load failures and an incompatible ABI report `UnsatisfiedLinkError` on first use. Linux and macOS extract the binary into `java.io.tmpdir`. Windows reuses a content-addressed DLL in `user.home/.vessel/native`. The native library remains loaded for the life of the JVM. The [native platform matrix](../../UserGuide/BuildFromSource.md#platforms-and-architectures) lists the target binaries.
