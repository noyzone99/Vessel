# Storage behavior

## Keys, metadata and payloads

An object consists of a key, a byte payload and optional file metadata such as the original filename and MIME type. A key like `documents/report.pdf` is a logical identifier. The slash is part of the key and can be used in ordinal, case-sensitive prefix searches such as `documents/`. Enumeration is lazy in .NET and uses a callback in the C API. Keys must be nonempty, contain a non-whitespace character and be representable as UTF-8 without embedded NUL characters.

Vessel keeps metadata in ZoneTree and stores payloads in a content-addressed directory. Identical bytes written with the same codec share a blob. Choose uncompressed storage, LZ4 or Zstd per write. `ObjectMetadata.Hash` and the native `hash` field identify the physical blob and include a codec suffix for compressed objects. `ObjectMetadata.ContentHash`, `VesselStream.Checksum` and `vessel_read` expose the SHA-256 digest of the original bytes.

## Open stores and readers

Open one Vessel instance per storage directory. Share that instance across concurrent operations and give each independent reader its own stream. Opening a second instance on the same directory fails with an I/O error. Wait for active operations to finish before reopening a disposed store.

An open stream reads the version it acquired, including its metadata, even if its key is later overwritten or deleted. Length refers to the uncompressed payload. Uncompressed streams support seeking. Compressed streams support sequential reads.

## Persistence and recovery

Vessel publishes and flushes a new payload before committing its metadata to a synchronous write-ahead log. A write cancelled before commit preserves the existing object, if any. Overwrite and delete commit their metadata changes before reclaiming unused payloads. On open, Vessel removes recognized temporary files and unreferenced blobs. These guarantees cover abrupt process exit on a local filesystem. Power-loss behavior depends on the filesystem and device.

Keep the store and its lock file together inside an application-controlled directory. Back up the entire closed directory before upgrading. Existing alpha stores migrate on first open by copying live metadata from `metadata` to `metadata-v1`. The original metadata directory is retained and existing blobs are reused. Use the same Vessel version for every process that opens a given store.
