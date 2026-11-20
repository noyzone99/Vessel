# C# user guide

## Setup

Use `Vessel.Core` in a project targeting .NET 8 or .NET 10. For a published alpha, install the exact version from [NuGet.org](https://www.nuget.org/packages/Vessel.Core) or download the `.nupkg` from the matching [GitHub Release](https://github.com/noyzone99/Vessel/releases) and use its directory as a local NuGet source.

~~~sh
dotnet new console -o artifacts/VesselQuickstart -f net10.0
dotnet add artifacts/VesselQuickstart/VesselQuickstart.csproj package Vessel.Core --version 0.0.1-alpha.1
cd artifacts/VesselQuickstart
~~~

To try a version before publication, use a project reference to `Sources/Vessel.Core/Vessel.Core.csproj` or [build the NuGet package from source](../BuildFromSource.md#build-the-managed-library).

## Write and read an object

Replace `Program.cs` with the [C# quick start](../../../README.md#quick-start) and run `dotnet run`. It prints `Hello from Vessel!` and creates a `my-store` directory under the current working directory. `overwrite: true` lets you run the example again with the same key.

Pass any readable stream to `PutAsync`, including a stream that cannot seek. Vessel reads from its current position to the end and leaves disposal to the caller. `GetStream` returns a read-only stream that owns an open reader for one object version. Dispose it when you finish reading.

## Common tasks

| Task | API |
| --- | --- |
| Attach file metadata | Pass `fileInfo: new ObjectInfo("hello.txt", ".txt", "text/plain")` to `PutAsync`. |
| Compress a payload | Pass `compressionType: CompressionType.LZ4` or `CompressionType.Zstd`. |
| Stop an upload | Pass a `cancellationToken` to `PutAsync`. |
| Inspect metadata | Call `storage.GetMetadata(key)`. It returns `null` for a missing key. |
| List related keys | Iterate `storage.GetAllKeys("documents/")`. |
| Delete an object | Await `storage.DeleteAsync(key)`. The result tells you whether an object was removed. |

See the [C# API reference](../../ApiReferences/CSharp/README.md) for types, defaults and errors, and [storage behavior](../Storage.md) for concurrency and persistence.
