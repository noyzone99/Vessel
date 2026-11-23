# User guide

| Language | How to use Vessel | Guide |
| --- | --- | --- |
| C# | `VesselStorage` NuGet package on .NET 8, .NET 10 or .NET 11 | [C#](CSharp/README.md) |
| F# | `VesselStorage` NuGet package on .NET 8, .NET 10 or .NET 11 | [F#](FSharp/README.md) |
| Visual Basic | `VesselStorage` NuGet package on .NET 8, .NET 10 or .NET 11 | [Visual Basic](VisualBasic/README.md) |
| C | NativeAOT shared library and C header | [C](C/README.md) |
| C++ | C++17 header-only binding and native shared library | [C++](Cpp/README.md) |
| Rust | `vessel-storage` Cargo crate from source and native shared library | [Rust](Rust/README.md) |
| Go | Go module from source and native shared library linked with cgo | [Go](Go/README.md) |
| Python | `vessel-storage` wheel with the native shared library included | [Python](Python/README.md) |
| Java | `io.github.noyzone99:vessel-storage` on Java 25 with bundled native libraries | [Java](Java/README.md) |
| Kotlin/JVM | Same `vessel-storage` JAR as Java, on JDK 25 | [Kotlin/JVM](Kotlin/README.md) |
| Node.js | `vessel-storage` on Node.js 24 and later, with TypeScript declarations and platform-specific native packages built from source | [Node.js](NodeJS/README.md) |

Find native C archives, NuGet packages, Python wheels and Java JARs under [GitHub Releases](https://github.com/noyzone99/Vessel/releases). The language guides give installation commands for [NuGet.org](https://www.nuget.org/packages/VesselStorage), [PyPI](https://pypi.org/project/vessel-storage/) and Maven Central once the relevant version is published.

Each guide uses the same quickstart. Open `my-store`, write `Hello from Vessel!` to `documents/hello.txt` with overwrite enabled, then read the object and print it.

Other languages with C interoperability can use the [C ABI reference](../ApiReferences/C/README.md) to implement a binding.

- [Build from source](BuildFromSource.md)
- [Storage behavior](Storage.md)
- [API references](../ApiReferences/README.md)
