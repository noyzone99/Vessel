# Rust user guide

## Setup

Use Rust 1.85 or later and the [`vessel-storage` crate](../../../Bindings/rust/) from a Vessel source checkout. Add its path as a Cargo dependency and import the library as `vessel`.

```toml
[dependencies]
vessel-storage = { path = "/path/to/Vessel/Bindings/rust" }
```

Choose a [native release archive](https://github.com/noyzone99/Vessel/releases), [CI artifact](../BuildFromSource.md#platforms-and-architectures) or [source build](../BuildFromSource.md#build-the-shared-library) that matches the target OS, architecture and Linux C library. Set `VESSEL_LIBRARY_DIR` to the directory containing the linker input for your target.

## Write and read an object

[The example](../../../Examples/rust/src/main.rs) opens `my-store`, writes the UTF-8 bytes of `Hello from Vessel!` to `documents/hello.txt` and streams the object to standard output. It enables overwrite so you can run it again.

On Linux or macOS, set `VESSEL_LIBRARY_DIR` to the absolute path of the extracted archive and run from the repository root.

```sh
export VESSEL_LIBRARY_DIR="/absolute/path/to/extracted-archive"
export LD_LIBRARY_PATH="$VESSEL_LIBRARY_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export DYLD_LIBRARY_PATH="$VESSEL_LIBRARY_DIR${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
cargo run --manifest-path Examples/rust/Cargo.toml
```

On Windows, use the Rust MSVC toolchain for your target and point `VESSEL_LIBRARY_DIR` at the directory containing `vessel.lib`. Point `PATH` at the directory containing `vessel.dll`. For a source build, the import library is in the `native` directory alongside `publish`.

```powershell
$nativeDir = 'C:\path\to\extracted-archive'
$env:VESSEL_LIBRARY_DIR = $nativeDir
$env:PATH = "$nativeDir;$env:PATH"
cargo run --manifest-path Examples/rust/Cargo.toml
```

## Common tasks

| Task | API |
| --- | --- |
| Stream a file | Pass a `std::fs::File` to `store.put`, and use `std::io::copy` to read from the `Reader` returned by `open_read`. |
| Attach file metadata | Set `WriteOptions::info` with an `ObjectInfo` containing the file name, extension or MIME type. |
| Compress a payload | Set `WriteOptions::compression` to `Compression::Zstd` or `Compression::Lz4`. |
| Inspect metadata | Call `store.metadata(key)` or `reader.metadata()` for the opened version. |
| List related keys | Call `store.keys("documents/")`. |
| Delete an object | Call `store.delete(key)`. The result indicates whether an object was removed. |

## See also

- [Rust API reference](../../ApiReferences/Rust/README.md)
- [Shared storage behavior](../Storage.md)
- [C ABI reference](../../ApiReferences/C/README.md)
