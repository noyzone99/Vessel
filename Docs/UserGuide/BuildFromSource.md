# Build Vessel from source

Use the .NET 10 SDK selected by [`global.json`](../../global.json). Run the build commands from the repository root.

## Build the managed library

~~~sh
dotnet build Vessel.slnx -c Release
dotnet pack Sources/Vessel.Core -c Release
~~~

`Vessel.Core` targets .NET 8 and .NET 10. Use a project reference to develop against the checkout. To consume the package, add `Sources/Vessel.Core/bin/Release/Packages/` as a local NuGet source alongside your existing sources and install `Vessel.Core` from it.

## Platforms and architectures

The platform matrix applies to the native library and language bindings that load it. The process that loads the library must match the binary's operating system, architecture and, on Linux, C library. The [native CI workflow](../../.github/workflows/validate.yml) builds each target, runs the C and Python ABI smoke tests, and tests the Python wheel in a fresh virtual environment.

| Platform | Architecture | .NET runtime identifier (RID) | Shared library | CI runner |
| --- | --- | --- | --- | --- |
| Windows | x64 | `win-x64` | `vessel.dll` | `windows-2022` |
| Windows | ARM64 | `win-arm64` | `vessel.dll` | `windows-11-vs2026-arm` |
| Linux (glibc) | x64 | `linux-x64` | `libvessel.so` | `ubuntu-24.04` |
| Linux (glibc) | ARM64 | `linux-arm64` | `libvessel.so` | `ubuntu-24.04-arm` |
| Linux (musl, e.g. Alpine) | x64 | `linux-musl-x64` | `libvessel.so` | `ubuntu-24.04` with Alpine |
| Linux (musl, e.g. Alpine) | ARM64 | `linux-musl-arm64` | `libvessel.so` | `ubuntu-24.04-arm` with Alpine |
| macOS (Intel) | x64 | `osx-x64` | `libvessel.dylib` | `macos-15-intel` |
| macOS (Apple Silicon) | ARM64 | `osx-arm64` | `libvessel.dylib` | `macos-15` |

After the smoke tests pass, CI uploads the shared library as `vessel-native-dynamic-<platform>-<arch>-<version>` with `vessel.h` and the license. Linux names include the C library, as in `vessel-native-dynamic-linux-musl-arm64-0.0.1-alpha.1`. Windows dynamic artifacts also include the `vessel.lib` import library. GitHub Releases carries the same files in ZIP archives.

CI also builds and tests experimental static artifacts named `vessel-native-static-windows-x64-<version>` and `vessel-native-static-linux-glibc-x64-<version>`. Each contains a static archive, the C header, linker options and license notices. See the [C linking instructions](C/README.md#link-an-experimental-static-library).

Linux glibc and musl builds produce separate binaries with the same filename. Choose the build environment to match the oldest userspace you intend to support. The Python wheels target glibc 2.39 or newer, musl 1.2 or newer, and macOS 15 or newer for the corresponding platforms. CI checks Linux wheels with auditwheel and macOS wheels with delocate before installing and testing them. [NativeAOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) · [Python platform tags](https://packaging.python.org/en/latest/specifications/platform-compatibility-tags/)

## Build the shared library

Publish on a host or in a container matching the target OS. Install the native toolchain for the target architecture. Use the Visual Studio Desktop development with C++ workload on Windows, Clang and `zlib1g-dev` on Ubuntu, and the Xcode toolchain on macOS. Windows x64 can build Windows ARM64 with the Visual Studio ARM64 C++ tools. [NativeAOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites) · [NativeAOT cross-compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)

For Alpine Linux, use the `10.0-alpine-aot` SDK container shown in the [Docker instructions](#build-linux-targets-from-windows-with-docker-desktop).

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
  'tar -C /src --exclude=bin --exclude=obj -cf /tmp/vessel-src.tar global.json Directory.Build.props LICENSE Sources/Vessel.Core Sources/Vessel.Native'
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

## Build a static library

Run the static build on the matching x64 platform with Python 3.10 or later and the .NET 10 SDK. Use an x64 Developer PowerShell on Windows, or Clang and GNU binutils on Linux glibc. The build script links and runs the C smoke client and checks its dynamic dependencies before staging the package.

~~~sh
python .github/scripts/build_static_native.py win-x64 artifacts/native-static/windows-x64
python .github/scripts/build_static_native.py linux-x64 artifacts/native-static/linux-glibc-x64
~~~

## Build a Python wheel

Use Python 3.10 or later with `venv` and `pip`, and install `patchelf` on Linux. Publish the [shared library](#build-the-shared-library) for your target first. Run the wheel builder in an environment matching the target OS, architecture and Linux C library. The script installs its build tools in a temporary virtual environment and tests the resulting wheel in a separate environment.

For Windows x64, run the following command from the repository root after publishing the shared library.

~~~sh
python .github/scripts/build_python_wheel.py win-x64 Sources/Vessel.Native/bin/Release/net10.0/win-x64/publish/vessel.dll artifacts/wheels
~~~

For other targets, use the RID and shared library filename from the [platform matrix](#platforms-and-architectures). The script writes the wheel to `artifacts/wheels` in this example.

## Tests and benchmarks

~~~sh
dotnet test Tests/Vessel.Core.UnitTests -c Release
dotnet run --project Tests/Vessel.Benchmark -c Release -- --quick
dotnet run --project Tests/Vessel.Benchmark -c Release -- --full
~~~

The default `--quick` profile covers reads, unique writes, overwrites and concurrent mixed traffic. `--full` runs the same scenarios across three payload sizes, three codecs, two worker counts and the .NET and NativeAOT runtimes. BenchmarkDotNet compiles the core into the NativeAOT benchmark process. The separately linked clients exercise the C ABI.

The fixtures mix compressible and random data and include a subset of frequently read keys. New writes use fresh payloads, and the write fixtures are cleaned between iterations. Results are reported per workload operation. A mixed churn operation includes a write and a delete. Set `VESSEL_BENCH_SEED` to an integer for a different reproducible workload. BenchmarkDotNet flags such as `--filter` work with both profiles.

### Smoke tests

The [C smoke test](../../Tests/Vessel.Native.Tests/smoke.c) exercises callback uploads, stream reads and ABI layout. The [raw Python smoke test](../../Tests/Vessel.Native.Tests/smoke.py) exercises the C ABI using `ctypes`, including process-exit recovery. On Linux or macOS, `smoke.sh` builds the C client with Clang and runs both ABI smoke tests and the Python package tests. The validation workflow includes the MSVC commands for Windows.

~~~sh
python Tests/Vessel.Native.Tests/smoke.py /path/to/libvessel.so
sh Tests/Vessel.Native.Tests/smoke.sh /path/to/libvessel.so
~~~

To run only the Python package tests, [install the package](Python/README.md#setup), set `VESSEL_LIBRARY_PATH` to a matching shared library and run `python -m unittest discover -s Bindings/python/tests` from the repository root.

## Tagged releases

Set the .NET and native version in [`Directory.Build.props`](../../Directory.Build.props) and its Python equivalent in [`pyproject.toml`](../../Bindings/python/pyproject.toml). For the first alpha, the version is `0.0.1-alpha.1` and the Python equivalent is `0.0.1a1`. The C ABI version is tracked separately in [`vessel.h`](../../Sources/Vessel.Native/vessel.h).

A tag matching the configured version, such as `v0.0.1-alpha.1`, triggers the [release jobs](../../.github/workflows/validate.yml) after validation. The [GitHub Release](https://github.com/noyzone99/Vessel/releases) contains eight dynamic and two experimental static C archives, the `Vessel.Core` NuGet package and eight `vessel-storage` wheels. Each wheel contains the shared library for its target. PyPI receives those same wheels.

Registry publication uses the `NUGET_API_KEY` secret in the `nuget` GitHub environment and a PyPI Trusted Publisher for `noyzone99/Vessel`, workflow `validate.yml` and environment `pypi`. The GitHub Release is available even if a registry needs its credentials configured. [NuGet publication](https://learn.microsoft.com/en-us/nuget/nuget-org/publish-a-package) · [PyPI Trusted Publishers](https://docs.pypi.org/trusted-publishers/adding-a-publisher/)
