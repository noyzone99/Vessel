# Vessel

Vessel is an embedded object store for local filesystems. It stores named objects with optional file metadata, per-object LZ4 or Zstd compression, deduplication and prefix enumeration. The `VesselStorage` NuGet package supports .NET 8, .NET 10 and .NET 11.

~~~csharp
using System.Text;
using Vessel;

using var storage = new VesselStorage("my-store");
await using var input = new MemoryStream(Encoding.UTF8.GetBytes("Hello from Vessel!"));
var metadata = await storage.PutAsync("documents/hello.txt", input, overwrite: true);

await using var content = storage.GetStream(metadata.Key);
using var reader = new StreamReader(content, Encoding.UTF8);
Console.WriteLine(await reader.ReadToEndAsync());
~~~

Open one Vessel instance per directory. Independent operations on that instance can run concurrently. A stream represents the version that was current when it opened, including metadata and payload. Vessel uses a synchronous write-ahead log for metadata and reclaims unreferenced files when opening a store.

Existing alpha stores migrate on first open. Back up the closed store before upgrading and keep every process using the same version of Vessel.

F# and Visual Basic use the same package and managed API.
