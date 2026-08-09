# Vessel

Vessel is a high-performance, in-process object storage library written in C#, compiled via Native AOT, and exported as a C-compatible native library. 

While implemented in C#, Vessel is **not exclusive to .NET**. It is designed from the ground up to be language-agnostic, providing a stable C ABI (Application Binary Interface) that makes it seamlessly usable from C, C++, Rust, Python, Go, Node.js, Java, and any other language that supports C interoperability.

Vessel is not a standalone service or a daemon process. It runs inside your application process as a lightweight, core storage primitive: a high-performance storage engine you embed directly into your code.

## Why Vessel exists

Many applications need more than a simple file dump or heavy database:

- They need durable, low-latency object persistence
- They need metadata stored alongside the payload
- They need fast lookups and efficient prefix-based iteration
- They need built-in support for compression and high-concurrency access
- They need an embeddable storage primitive that lives directly inside the application process without external dependencies

Vessel was created to provide these capabilities through a single, highly-optimized native engine focused on performance, zero-allocation memory management, and cross-language portability.

## Core ideas

Vessel is built around a few simple but powerful design choices:

- **Native & Cross-Language First:** Built with Native AOT in mind, exposing a clean C ABI for universal interoperability across runtimes and languages.
- **In-Process Engine Model:** Embedded directly into your host process, eliminating network overhead and IPC latency.
- **Logical Key-Based Storage:** Objects are addressed by stable logical keys rather than ad-hoc file paths.
- **Metadata-First Indexing:** Object metadata is managed independently from the payload, enabling fast queries and efficient inspection.
- **Balanced Disk Layout:** Data files are distributed across a multi-level hashed directory structure to prevent filesystem hot spots.
- **Per-Object Compression:** Configurable compression applied atomically during write operations (LZ4, Zstd).
- **Concurrency & Robustness:** Designed for high-throughput parallel writes and predictable local persistence.

## What Vessel is and is not

**Vessel is:**
- A high-performance, embeddable object storage engine exported as a C-compatible library.
- A native storage primitive for applications written in C#, C/C++, Rust, Python, Go, Node.js, and more.
- An application-scoped local persistence engine.

**Vessel is not:**
- A standalone server process or daemon.
- A distributed storage system.
- A cloud object storage platform (e.g., S3 replacement).
- A relational database replacement.

## What Vessel offers today

- Simple stream-based API for storing (`Put`) and retrieving (`Get`) objects.
- Metadata indexing attached directly to stored payloads.
- Key prefix enumeration and efficient object lookup.
- File metadata tagging (filename, extension, MIME type).
- Per-object, configurable compression (LZ4 and Zstd).
- Native AOT execution with exported C function bindings.
- Direct idiomatic .NET bindings alongside native interop capabilities.

## Quick start (.NET / C#)

While Vessel can be loaded dynamically into any language via its C ABI, it provides first-class, high-level .NET bindings:

```csharp
using System.Text;
using Vessel;

var storage = new VesselStorage("/path/to/storage");

await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello from Vessel"));

var metadata = await storage.PutAsync(
    "documents/hello.txt",
    stream,
    overwrite: false,
    new ObjectInfo("hello.txt", ".txt", "text/plain"),
    CompressionType.Zstd,
    CompressionLevel.High);

var objectStream = storage.GetStream("documents/hello.txt");
using var reader = new StreamReader(objectStream, Encoding.UTF8);
Console.WriteLine(await reader.ReadToEndAsync());

```

## Architecture at a glance

Vessel separates three concerns:

1. **The API Layer:** High-level language wrappers and C ABI functions exposed to host applications.
2. **The Metadata Repository:** In-process indexing engine for fast lookup and enumeration.
3. **The Blob Storage Engine:** Responsible for persisting and retrieving compressed payload bytes on disk.

This clean separation ensures low overhead across interop boundaries and allows key components to be tuned for maximum performance.

## Project status

> ⚠️ **Under Active Development:** Vessel is currently in an early alpha phase. The core storage model and native interop layer are functional, but **APIs are subject to breaking changes** as the architecture matures.

With the release of the first pre-stable version, the repository will be updated to include:

* Multi-language implementation examples and guides (C/C++, Rust, Python, Node.js).
* Detailed performance benchmarks and zero-allocation profiling metrics.

## Roadmap

Near-term focus areas:

* Finalizing C headers and official high-level bindings for C/C++, Python, Rust, and Node.js.
* Replacing standard compression implementations with NativeCompressions for zero-allocation performance.
* Replacing the logging pipeline with a zero-allocation, AOT-friendly logging abstraction.
* Evaluating Blake3 hashing against the current hashing strategy.
* Adding HMAC-SHA256 support to mitigate filesystem denial-of-service risks.
* Smart compression filters to automatically skip incompressible media types.
* Standardizing compression interfaces across all targets.
* Support for `Pipelines` (`PipeWriter` / `PipeReader`) in streaming APIs (exclusive to the .NET interface).
* Support for `Microsoft.Extensions.Hosting` and dependency injection in hosting scenarios (exclusive to the .NET interface).

## Acknowledgements

Vessel builds on principles and software from excellent open-source projects, including ZoneTree, K4os.Compression.LZ4, and ZstdSharp.Port.

## License

Vessel is distributed under the MIT License.