using System.Text;
using Vessel;

const string message = "Hello from Vessel!";
using var storage = new VesselStorage("my-store");
using var input = new MemoryStream(Encoding.UTF8.GetBytes(message));
var metadata = await storage.PutAsync("documents/hello.txt", input, overwrite: true);

await using var content = storage.GetStream(metadata.Key);
using var reader = new StreamReader(content, Encoding.UTF8, leaveOpen: true);
var stored = await reader.ReadToEndAsync();
if (stored != message)
    throw new InvalidDataException("Stored text differs from the original.");

Console.WriteLine(stored);
