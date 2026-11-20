# User guide

| Language | How to use Vessel | Guide |
| --- | --- | --- |
| C# | `VesselStorage` NuGet package on .NET 8, .NET 10 or .NET 11 | [C#](CSharp/README.md) |
| C | NativeAOT shared library and C header | [C](C/README.md) |
| Python | `vessel-storage` wheel with the native shared library included | [Python](Python/README.md) |
| Java | `io.github.berik-99:vessel-storage` on Java 25 with bundled native libraries | [Java](Java/README.md) |

Find native C archives, NuGet packages, Python wheels and Java JARs under [GitHub Releases](https://github.com/berik-99/Vessel/releases). The language guides give installation commands for [NuGet.org](https://www.nuget.org/packages/VesselStorage), [PyPI](https://pypi.org/project/vessel-storage/) and Maven Central once the relevant version is published.

Node.js bindings are planned. Other languages with C interoperability can use the [C ABI reference](../ApiReferences/C/README.md) to implement a binding.

- [Build from source](BuildFromSource.md)
- [Storage behavior](Storage.md)
- [API references](../ApiReferences/README.md)
