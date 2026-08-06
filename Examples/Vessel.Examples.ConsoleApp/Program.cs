using System.Text;
using Vessel;
using Vessel.Models;

Console.WriteLine("Esempio di utilizzo realistico di VesselStorage\n");

var storageRoot = Path.Combine(AppContext.BaseDirectory, ".storage-demo");
var storage = new VesselStorage(storageRoot);

var documents = new[]
{
    new
    {
        Key = "invoices/2026-01",
        FileName = "invoice-2026-01.json",
        Content = "{\"invoice\":\"INV-001\",\"amount\":1250.50,\"status\":\"paid\"}",
        CompressionType = CompressionType.LZ4,
        CompressionLevel = CompressionLevel.Balanced
    },
    new
    {
        Key = "reports/summary",
        FileName = "summary.txt",
        Content = "Quarterly summary:\n- Revenue up 18%\n- New customers 42\n- Churn stable",
        CompressionType = CompressionType.Zstd,
        CompressionLevel = CompressionLevel.High
    },
    new
    {
        Key = "documents/notes",
        FileName = "notes.md",
        Content = "# Meeting notes\n- align with product\n- ship by end of month",
        CompressionType = CompressionType.None,
        CompressionLevel = CompressionLevel.Balanced
    }
};

Console.WriteLine("Scrittura concorrente di oggetti...");
var uploadTasks = documents.Select(document => Task.Run(async () =>
{
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.Content));
    return await storage.PutAsync(
        document.Key,
        stream,
        overwrite: false,
        new ObjectInfo(document.FileName, Path.GetExtension(document.FileName), "application/octet-stream"),
        document.CompressionType,
        document.CompressionLevel);
}));

var uploadedMetadata = await Task.WhenAll(uploadTasks);

Console.WriteLine("\nChiavi presenti nel repository:");
await foreach (var key in storage.GetAllKeysAsync())
{
    Console.WriteLine($"- {key}");
}

Console.WriteLine("\nLettura dei file e dei metadati:");
foreach (var metadata in uploadedMetadata)
{
    await using var response = await storage.GetAsync(metadata.Key);
    using var reader = new StreamReader(response.ContentStream, Encoding.UTF8, leaveOpen: false);
    var content = await reader.ReadToEndAsync();

    Console.WriteLine($"\nKey: {metadata.Key}");
    Console.WriteLine($"FileName: {response.Metadata.FileName}");
    Console.WriteLine($"Compression: {response.Metadata.CompressionType}");
    Console.WriteLine($"Content: {content}");
}

Console.WriteLine("\nRecupero metadati di una chiave esistente:");
var invoiceMetadata = await storage.GetMetadataAsync("invoices/2026-01");
Console.WriteLine($"Metadata found: {invoiceMetadata?.FileName} / {invoiceMetadata?.CompressionType}");

Console.WriteLine("\nTentativo di lettura di una chiave inesistente:");
try
{
    await using var missingResponse = await storage.GetAsync("missing/key");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"Errore atteso: {ex.Message}");
}

Console.WriteLine("\nEsempio completato.");