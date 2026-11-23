# Go user guide

## Setup

Use Go 1.24 or later, a cgo-compatible C compiler and a checkout of [the Go module](../../../Bindings/go/). Set `CGO_ENABLED=1`. For a separate local Go project, use a `replace` directive pointing to the checkout's `Bindings/go` directory, as shown in the [example module](../../../Examples/go/go.mod).

Use the [native release archive](https://github.com/noyzone99/Vessel/releases), a [CI artifact](../BuildFromSource.md#platforms-and-architectures) or a [source build](../BuildFromSource.md#build-the-shared-library) that matches the Go target OS, architecture and Linux C library. Set the linker search path in `CGO_LDFLAGS` and configure the runtime library search path. The full checkout supplies `vessel.h`. If you consume the module outside the full checkout, set `CGO_CPPFLAGS` to include the directory with the matching `vessel.h`.

## Write and read an object

The [example](../../../Examples/go/main.go) opens `my-store`, writes `Hello from Vessel!` to `documents/hello.txt` with overwrite enabled and prints the text after reading it back. Run it again to replace the object at the same key.

On Linux or macOS, point `native_dir` at the directory containing the shared library and run from the repository root.

```sh
native_dir=/absolute/path/to/extracted-archive
export CGO_ENABLED=1
export CGO_LDFLAGS="-L$native_dir"
export LD_LIBRARY_PATH="$native_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export DYLD_LIBRARY_PATH="$native_dir${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}"
cd Examples/go
go run .
```

On Windows, use a MinGW-w64 compiler for the target architecture. Point `$nativeDir` at the extracted archive and the import library path at the directory containing `vessel.lib`. For a source build, the import library is in the sibling `native` directory.

```powershell
$nativeDir = 'C:/path/to/extracted-archive'
$env:CGO_ENABLED = '1'
$env:CC = 'gcc'
$env:CGO_LDFLAGS = "-L$nativeDir"
$env:PATH = "$nativeDir;$env:PATH"
Set-Location Examples/go
go run .
```

When cross-compiling, set `GOOS` and `GOARCH` to the target, `CC` to the target C compiler and the cgo flags to the target header and library directories. Use a toolchain and native binary for the same Linux C library when targeting glibc or musl.

## Common tasks

| Task | API |
| --- | --- |
| Stream a file | Pass an open `*os.File` to `Put`. Use `io.Copy` with the `Stream` from `Open` to download, and close both files and the `Stream`. |
| Attach file metadata | Set `FileName`, `Extension` and `MIMEType` on `WriteOptions`. |
| Compress a payload | Set `Compression` to `vessel.LZ4` or `vessel.Zstd`. |
| Inspect metadata | Call `store.Metadata(key)`. A missing key returns `vessel.ErrNotFound`. |
| List related keys | Call `store.Keys("documents/")`. |
| Delete an object | Call `store.Delete(key)`. The boolean reports whether the key existed when the call succeeds. |

## See also

- [Go API reference](../../ApiReferences/Go/README.md)
- [Shared storage behavior](../Storage.md)
- [C ABI reference](../../ApiReferences/C/README.md)
