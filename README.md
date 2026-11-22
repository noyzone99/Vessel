<h1>
  <img src="Docs/Assets/vessel-banner.png" width="100%" alt="Vessel. Object storage, built into your app.">
</h1>

Vessel is a lightweight, embedded object store for local filesystems. It runs in your application process and stores byte streams under logical keys, with optional file metadata and per-object compression.

## Why Vessel?

Applications that keep documents, media or other binary data on disk often need to look up metadata, list related objects and replace content without interrupting readers. Vessel gives those applications a single storage API and manages how metadata and payloads are committed and recovered.

Vessel is designed to keep memory use predictable while streaming large objects and to avoid the overhead of running a separate storage service.

- **Stream large objects.** Read and write payloads incrementally. Look up metadata and list keys by prefix without opening payloads.
- **Control disk usage.** Choose LZ4, Zstd or uncompressed storage for each object. Identical payloads written with the same codec share a blob.
- **Embed the same engine across languages.** Use the managed library from .NET 8, .NET 10 or .NET 11, or load the NativeAOT shared library through its C ABI.

## Getting started

- [C#](Docs/UserGuide/CSharp/README.md) uses the `VesselStorage` NuGet package on .NET 8, .NET 10 or .NET 11.
- [C](Docs/UserGuide/C/README.md) uses `vessel.h` and the native library.
- [Python](Docs/UserGuide/Python/README.md) uses the `vessel-storage` wheel with its bundled native library.
- [Java](Docs/UserGuide/Java/README.md) uses the `vessel-storage` JAR on Java 25 with bundled native libraries.
- [Node.js](Docs/UserGuide/NodeJS/README.md) uses the `vessel-storage` npm package on Node.js 24 and later with JavaScript and TypeScript support. The npm packages can be built from source.

For published versions, find the native C archives, NuGet package, Python wheels and Java JARs in [GitHub Releases](https://github.com/noyzone99/Vessel/releases). The [user guide](Docs/UserGuide/README.md) links to the package registries.

## Build from source

Install the .NET 11 SDK selected by [`global.json`](global.json). Building the native library also requires the target platform's C/C++ toolchain. Use the Visual Studio **Desktop development with C++** workload on Windows, Clang and `zlib1g-dev` on Ubuntu, or the Xcode toolchain on macOS. The [source build guide](Docs/UserGuide/BuildFromSource.md#build-the-shared-library) also covers Alpine Linux and the supported runtime identifiers.

Run `dotnet build Vessel.slnx -c Release` from the repository root to build the .NET solution. To build the packages and native library, follow the instructions for your language.

- **C#** — [Build the `VesselStorage` NuGet package](Docs/UserGuide/BuildFromSource.md#build-the-managed-library).
- **C** — [Publish the shared library](Docs/UserGuide/BuildFromSource.md#build-the-shared-library) or [build an experimental static library](Docs/UserGuide/BuildFromSource.md#build-a-static-library) with Python 3.10 or later.
- **Python** — [Build a wheel from the shared library](Docs/UserGuide/BuildFromSource.md#build-a-python-wheel). This requires Python 3.10 or later with `venv` and `pip`, plus `patchelf` on Linux.
- **Java** — [Build the JAR with Maven](Docs/UserGuide/BuildFromSource.md#build-the-java-jar). This requires JDK 25 and the shared library for each platform you want to package.
- **Node.js** — [Build the npm packages](Docs/UserGuide/BuildFromSource.md#build-the-nodejs-packages). This requires Node.js 24 or later, a native compiler and the shared library for your platform.

## Documentation

- [Documentation index](Docs/README.md)
- [User guide](Docs/UserGuide/README.md)
- [API references](Docs/ApiReferences/README.md)
- [Storage behavior and recovery](Docs/UserGuide/Storage.md)

## Acknowledgments

Thanks to the maintainers and contributors of the projects that Vessel relies on.

- Object metadata indexing and persistence use [ZoneTree](https://github.com/ZoneTree/ZoneTree).
- LZ4 payload compression uses [K4os.Compression.LZ4 and K4os.Compression.LZ4.Streams](https://github.com/MiloszKrajewski/K4os.Compression.LZ4).
- Zstandard payload compression uses [`ZstdSharp.Port`](https://github.com/oleg-st/ZstdSharp).
- Performance benchmarks use [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet).

## License

Vessel is licensed under the [MIT License](LICENSE).
