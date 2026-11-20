# User guide

| Language | How to use Vessel | Guide |
| --- | --- | --- |
| C# | Managed `Vessel.Core` library on .NET 8 or .NET 10 | [C#](CSharp/README.md) |
| C | NativeAOT shared library and C header | [C](C/README.md) |
| Python | `vessel-storage` wheel with the native shared library included | [Python](Python/README.md) |

Find native C archives, NuGet packages and Python wheels under [GitHub Releases](https://github.com/noyzone99/Vessel/releases). The language guides also give the installation commands for [NuGet.org](https://www.nuget.org/packages/Vessel.Core) and [PyPI](https://pypi.org/project/vessel-storage/). These download and registry links apply once the tagged version is published.

Java and Node.js bindings are planned. Other languages with C interoperability can use the [C ABI reference](../ApiReferences/C/README.md) to implement a binding.

- [Build from source](BuildFromSource.md)
- [Storage behavior](Storage.md)
- [API references](../ApiReferences/README.md)
