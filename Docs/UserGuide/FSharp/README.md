# F# user guide

## Setup

Use the `VesselStorage` NuGet package from an F# project targeting .NET 8, .NET 10 or .NET 11. For a published alpha, install the exact version from [NuGet.org](https://www.nuget.org/packages/VesselStorage) or the matching [GitHub Release](https://github.com/noyzone99/Vessel/releases).

```sh
dotnet new console -lang F# -f net11.0 -o artifacts/VesselFSharpQuickstart
dotnet add artifacts/VesselFSharpQuickstart/VesselFSharpQuickstart.fsproj package VesselStorage --version 0.0.1-alpha.1
cd artifacts/VesselFSharpQuickstart
```

To use the checkout, run `dotnet run --project Examples/fsharp` from the repository root. The example uses a project reference to Vessel.

## Write and read an object

Copy the [example source](../../../Examples/fsharp/Program.fs) to `Program.fs` and run `dotnet run`. It opens `my-store`, writes `documents/hello.txt` with overwrite enabled and prints `Hello from Vessel!` after reading it back. Run it again to replace the object at the same key.

The example awaits `PutAsync` and `ReadToEndAsync` in a `task` computation expression. Pass a readable `Stream` to `PutAsync`, and use `use` to dispose the input, the stream returned by `GetStream` and the store.

## Common tasks

| Task | API |
| --- | --- |
| Attach file metadata | Pass `fileInfo = ObjectInfo("hello.txt", ".txt", "text/plain")` to `PutAsync`. |
| Compress a payload | Pass `compressionType = CompressionType.LZ4` or `CompressionType.Zstd`. |
| Stop an upload | Pass `cancellationToken = token` to `PutAsync`. |
| Inspect metadata | Call `storage.GetMetadata(key)` and check `HasValue` before reading `Value`. |
| List related keys | Iterate `storage.GetAllKeys("documents/")`. |
| Delete an object | Await `storage.DeleteAsync(key)` with `let!`. The result indicates whether the key existed. |

## See also

- [F# API reference](../../ApiReferences/FSharp/README.md)
- [Shared storage behavior](../Storage.md)
