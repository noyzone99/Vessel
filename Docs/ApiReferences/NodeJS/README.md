# Node.js API reference

`vessel-storage` exports `Vessel` and `VesselError` for ESM and CommonJS on Node.js 24 and later. See the [TypeScript declarations](../../../Bindings/node/index.d.cts) for exact signatures and the [Node.js guide](../../UserGuide/NodeJS/README.md) for installation.

## Vessel

| Method | Result and behavior |
| --- | --- |
| `Vessel.open(directory, options?)` | Opens or creates a store. `enableMetadataCompression` defaults to `false`. |
| `put(key, buffer, options?)` | Stores a Node.js `Buffer` and resolves to the committed metadata. Keep the buffer unchanged until the promise settles. |
| `createReadStream(key, options?)` | Returns a Node.js `Readable`. Once opened, the stream retains its object version across overwrite, delete and store close. |
| `createWriteStream(key, options?)` | Returns a Node.js `Writable`. A successful `finish` means the object was committed. Use `pipeline()` for source errors and cancellation. |
| `getMetadata(key)` | Returns metadata for the current version or rejects if the key does not exist. |
| `keys(prefix?)` | Returns an array of matching keys. The default empty prefix matches all keys. |
| `delete(key)` | Returns `true` if a key was removed and `false` if it was absent. |
| `close()` | Cancels uploads that have yet to request commit, waits for pending store calls and uploads, and releases the store. A repeated close returns the same promise. The class also implements `Symbol.asyncDispose`. |

Write options include `overwrite` (defaults to `false`), `compression` (`none`, `zstd` or `lz4`, defaults to `none`), `level` (`fastest`, `balanced`, `high` or `ultra`, defaults to `balanced`) and optional `fileName`, `extension` and `mimeType`. Terminating an upload before it requests commit preserves the previous object. An abort that arrives after the commit request can race with publication.

Read and write streams accept an `AbortSignal` and default to a 64 KiB `highWaterMark`. Uploads consume buffers in chunks and copy directly into the native pipeline. `Buffer` instances backed by `SharedArrayBuffer` are unsupported for writes. Close or destroy a stream you stop consuming before it finishes.

## ObjectMetadata

`originalSize` and `compressedSize` are byte counts represented as `bigint`. `createdAt` is a `Date` with millisecond precision. `hash` is the blob identifier, `contentHash` is the SHA-256 digest of the original payload, and `etag` identifies the stored version. File attributes are strings or `null`.

## VesselError

A native operation failure exposes the signed C ABI status in `status` and a string in `code`, such as `ERR_VESSEL_NOT_FOUND`, `ERR_VESSEL_ALREADY_EXISTS` or `ERR_VESSEL_DISPOSED`. Invalid JavaScript arguments can throw `TypeError`, and Node.js supplies its own stream errors, including `AbortError`. See the [C ABI reference](../C/README.md) for status meanings and native lifetimes.
