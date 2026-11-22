# C# API reference

`Vessel` targets .NET 8, .NET 10 and .NET 11. Storage and data types are in the `Vessel` namespace. Logging types are in `Vessel.Logging`.

## `VesselStorage` and `IVesselStorage`

The [public interface](../../../Sources/Vessel/IVesselStorage.cs) defines the operations below. [`VesselStorage`](../../../Sources/Vessel/VesselStorage.cs) implements them for a local directory.

| Member | Inputs, result and common failures |
| --- | --- |
| `VesselStorage(string storageBasePath, VesselStorageOptions? options = null)` | Opens or creates the directory. An open store already holding the directory lock causes an `IOException`. |
| `PutAsync(string key, Stream contentStream, ...)` | Reads the caller's stream from its current position to EOF and returns `Task<ObjectMetadata>`. A duplicate key with overwrite disabled causes `InvalidOperationException`. |
| `GetStream(string key)` | Returns a read-only `VesselStream` for a version of the object. A missing key causes `FileNotFoundException`. Missing or invalid blob data can cause `InvalidDataException`. |
| `GetMetadata(string key)` | Returns `ObjectMetadata?` without opening the blob and returns `null` for a missing key. |
| `GetAllKeys(string prefix = "")` | Returns a lazy `IEnumerable<string>` matching an ordinal prefix. The empty prefix matches all keys. `null` causes `ArgumentNullException`. |
| `DeleteAsync(string key, CancellationToken cancellationToken = default)` | Returns `Task<bool>` with `true` if an object was deleted or `false` if the key was absent. |
| `Dispose()` | Stops accepting operations and releases the store after active operations finish. Calls on a disposed store throw `ObjectDisposedException`. |

`PutAsync` defaults to `overwrite: false`, `fileInfo: default`, `compressionType: CompressionType.None`, `compressionLevel: CompressionLevel.Balanced` and `cancellationToken: default`. A null input stream raises `ArgumentNullException`. Invalid keys and out-of-range codec or level values raise argument exceptions. Cancellation observed before commit leaves the previous object intact and completes the task as cancelled. Dispose input streams yourself, and dispose or exhaust active enumerators before reopening the store.

After a metadata mutation fails, operations on the metadata index raise `IOException` until the store is closed and reopened. See [persistence and recovery](../../UserGuide/Storage.md#persistence-and-recovery).

## `VesselStream`

[`VesselStream`](../../../Sources/Vessel/VesselStream.cs) extends `Stream` and exposes `Metadata`, `Checksum`, `FileName`, `Extension` and `MimeType` for the opened version. `Length` is the uncompressed byte count. Check `CanSeek` before accessing `Position` or calling `Seek`. Compressed streams throw `NotSupportedException` for these operations. Use `Dispose` or `DisposeAsync` to close the reader. Use separate streams for independent read positions.

## Data and options

| Type | Contract |
| --- | --- |
| [`ObjectInfo`](../../../Sources/Vessel/ObjectInfo.cs) | Optional `FileName`, `Extension` and `MimeType` values supplied by the caller. |
| [`ObjectMetadata`](../../../Sources/Vessel/ObjectMetadata.cs) | `Key`, physical `Hash`, logical `ContentHash`, `ETag`, `OriginalSize`, `CompressedSize`, `CompressionType`, UTC `CreationDate` and optional file attributes. `CreationDate` is set when the object is written or replaced. |
| [`CompressionType`](../../../Sources/Vessel/CompressionType.cs) | `None`, `Zstd` or `LZ4`. |
| [`CompressionLevel`](../../../Sources/Vessel/CompressionLevel.cs) | `Fastest`, `Balanced`, `High` or `Ultra`. The selected codec determines the speed and compression trade-off. |
| [`VesselStorageOptions`](../../../Sources/Vessel/VesselStorageOptions.cs) | `EnableMetadataCompression` defaults to `false`. `Logger` defaults to `null`. |
| [`IVesselLogger`](../../../Sources/Vessel/Logging/IVesselLogger.cs) | Implements `Log(VesselLogLevel level, string message, Exception? exception = null)`. Levels are `Debug`, `Information`, `Warning` and `Error`. A shared logger should support concurrent calls. |

See the [C# user guide](../../UserGuide/CSharp/README.md) for setup and examples, and [storage behavior](../../UserGuide/Storage.md) for lifetime and recovery details.
