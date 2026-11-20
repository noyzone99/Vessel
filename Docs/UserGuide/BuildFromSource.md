# Build Vessel from source

Use the .NET 10 SDK selected by [`global.json`](../../global.json). Run the build commands from the repository root.

## Build the managed library

~~~sh
dotnet build Vessel.slnx -c Release
dotnet pack Sources/Vessel.Core -c Release
~~~

`Vessel.Core` targets .NET 8 and .NET 10. Use a project reference to develop against the checkout. To consume the package, add `Sources/Vessel.Core/bin/Release/Packages/` as a local NuGet source alongside your existing sources and install `Vessel.Core` from it.

## Platforms and architectures

The platform matrix applies to the native library and language bindings that load it. The process that loads the library must match the binary's operating system, architecture and, on Linux, C library. The [native CI workflow](../../.github/workflows/validate.yml) is configured to build each target and run the C and Python smoke tests.

| Platform | Architecture | .NET runtime identifier (RID) | Shared library | CI runner |
| --- | --- | --- | --- | --- |
| Windows | x64 | `win-x64` | `vessel.dll` | `windows-2022` |
| Windows | ARM64 | `win-arm64` | `vessel.dll` | `windows-11-vs2026-arm` |
| Linux (glibc) | x64 | `linux-x64` | `vessel.so` | `ubuntu-24.04` |
| Linux (glibc) | ARM64 | `linux-arm64` | `vessel.so` | `ubuntu-24.04-arm` |
| Linux (musl, e.g. Alpine) | x64 | `linux-musl-x64` | `vessel.so` | `ubuntu-24.04` with Alpine |
| Linux (musl, e.g. Alpine) | ARM64 | `linux-musl-arm64` | `vessel.so` | `ubuntu-24.04-arm` with Alpine |
| macOS (Intel) | x64 | `osx-x64` | `vessel.dylib` | `macos-15-intel` |
| macOS (Apple Silicon) | ARM64 | `osx-arm64` | `vessel.dylib` | `macos-15` |

After the smoke tests pass, CI uploads an artifact named `vessel-native-<RID>` containing the shared library and `vessel.h`. Windows artifacts also include the `vessel.lib` import library.

Linux glibc and musl builds produce separate binaries with the same filename. Choose the Linux build environment to match the oldest userspace you intend to support. When packaging Python wheels, use the `manylinux` or `musllinux` tag that matches the C library, minimum version and architecture verified for that wheel. [NativeAOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) · [Python platform tags](https://packaging.python.org/en/latest/specifications/platform-compatibility-tags/)

## Build the shared library

Publish on a host or in a container matching the target OS. Install the native toolchain for the target architecture. Use the Visual Studio Desktop development with C++ workload on Windows, Clang and `zlib1g-dev` on Ubuntu, and the Xcode toolchain on macOS. Windows x64 can build Windows ARM64 with the Visual Studio ARM64 C++ tools. [NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites) · [NativeAOT cross-compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)

Run one of these commands or substitute another RID from the matrix.

~~~sh
dotnet publish Sources/Vessel.Native -c Release -r win-x64
dotnet publish Sources/Vessel.Native -c Release -r linux-x64
dotnet publish Sources/Vessel.Native -c Release -r linux-musl-x64
dotnet publish Sources/Vessel.Native -c Release -r osx-arm64
~~~

The default publish directory is `Sources/Vessel.Native/bin/Release/net10.0/<RID>/publish/`. It contains the shared library and [`vessel.h`](../../Sources/Vessel.Native/vessel.h). Windows source builds produce `vessel.lib` in the sibling `native/` directory.

### Build Linux targets from Windows with Docker Desktop

Use Docker Desktop in Linux containers mode. The Microsoft `10.0-noble-aot` SDK image targets glibc and `10.0-alpine-aot` targets musl. Select `linux/amd64` for x64 or `linux/arm64` for ARM64. On a Windows x64 host, Docker Desktop can run the ARM64 container through emulation. [Microsoft .NET SDK images](https://github.com/dotnet/dotnet-docker/blob/main/README.sdk.md) · [Docker multi-platform builds](https://docs.docker.com/build/building/multi-platform/)

This PowerShell example builds `linux-musl-x64` from the repository root and writes the result to `artifacts/native/linux-musl-x64/`. The container copies the sources to its own working directory so its intermediate build files stay separate from the Windows build.

~~~powershell
$vesselRid = 'linux-musl-x64'
$vesselOutput = (New-Item -ItemType Directory -Force "artifacts/native/$vesselRid").FullName
$vesselBuild = @(
  'mkdir -p /build'
  'tar -C /src --exclude=bin --exclude=obj -cf /tmp/vessel-src.tar global.json Sources/Vessel.Core Sources/Vessel.Native'
  'tar -xf /tmp/vessel-src.tar -C /build'
  'cd /build'
  'dotnet publish Sources/Vessel.Native -c Release -r $VESSEL_RID -o /out'
) -join ' && '
docker run --rm --platform linux/amd64 `
  --mount "type=bind,source=$($PWD.Path),target=/src,readonly" `
  --mount "type=bind,source=$vesselOutput,target=/out" `
  --env "VESSEL_RID=$vesselRid" `
  mcr.microsoft.com/dotnet/sdk:10.0-alpine-aot sh -ec $vesselBuild
~~~

For Alpine ARM64, set `$vesselRid` to `linux-musl-arm64` and use `--platform linux/arm64`. For glibc, use `10.0-noble-aot` and the corresponding `linux-x64` or `linux-arm64` RID.

## Tests and benchmarks

~~~sh
dotnet test Tests/Vessel.Core.UnitTests -c Release
dotnet run --project Tests/Vessel.Benchmark -c Release -- --quick
dotnet run --project Tests/Vessel.Benchmark -c Release -- --full
~~~

The default `--quick` profile covers reads, unique writes, overwrites and concurrent mixed traffic. `--full` runs the same scenarios across three payload sizes, three codecs, two worker counts and the .NET and NativeAOT runtimes. BenchmarkDotNet compiles the core into the NativeAOT benchmark process. The separately linked clients exercise the C ABI.

The fixtures mix compressible and random data and include a subset of frequently read keys. New writes use fresh payloads, and the write fixtures are cleaned between iterations. Results are reported per workload operation. A mixed churn operation includes a write and a delete. Set `VESSEL_BENCH_SEED` to an integer for a different reproducible workload. BenchmarkDotNet flags such as `--filter` work with both profiles.

### Smoke tests

The [C smoke test](../../Tests/Vessel.Native.Tests/smoke.c) exercises callback uploads, stream reads and ABI layout. The [Python smoke test](../../Tests/Vessel.Native.Tests/smoke.py) exercises the native library using `ctypes`, including process-exit recovery. Run the Python test with the path to a matching library. On Linux or macOS, `smoke.sh` builds the C client with Clang and runs both tests. The validation workflow includes the MSVC commands for Windows.

~~~sh
python Tests/Vessel.Native.Tests/smoke.py /path/to/vessel.so
sh Tests/Vessel.Native.Tests/smoke.sh /path/to/vessel.so
~~~
