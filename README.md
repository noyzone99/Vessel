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

## Benchmarks

Benchmarks are performed using BenchmarkDotNet v0.15.8 on Windows 11 with an Intel Core Ultra 7 258V CPU. The benchmarks compare the performance of .NET 10.0 and NativeAOT 10.0 (the C-compatible native library) runtimes across various operations, including writing new objects, overwriting existing objects, and random reads, with different payload sizes and compression types (None, Zstd, LZ4).

| Method                                          | Job            | Runtime        | CompressionType | PayloadSize | Mean        | Error      | StdDev       | Median      | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------------------------ |--------------- |--------------- |---------------- |------------ |------------:|-----------:|-------------:|------------:|------:|--------:|--------:|-------:|----------:|------------:|
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | None            | 1024        |    39.37 us |   0.763 us |     1.885 us |    38.62 us |  1.00 |    0.07 |  0.2441 |      - |   2.63 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | None            | 1024        |    47.96 us |   4.416 us |    13.022 us |    41.14 us |  1.22 |    0.33 |  0.2441 |      - |   2.51 KB |        0.95 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | None            | 1024        | 1,862.65 us |  37.328 us |   107.701 us | 1,868.39 us |  1.00 |    0.09 |       - |      - |   5.11 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | None            | 1024        | 3,184.05 us | 401.260 us | 1,183.123 us | 2,356.45 us |  1.72 |    0.64 |       - |      - |      4 KB |        0.78 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | None            | 1024        |   186.51 us |  10.888 us |    32.103 us |   188.85 us |  1.03 |    0.25 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | None            | 1024        |    87.95 us |   1.693 us |     1.501 us |    87.48 us |  0.49 |    0.09 |  0.1221 |      - |   1.07 KB |        0.86 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | None            | 524288      | 3,004.80 us |  57.491 us |    74.754 us | 3,017.12 us |  1.00 |    0.03 |       - |      - |   3.91 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | None            | 524288      | 2,979.00 us |  58.803 us |   117.436 us | 2,969.93 us |  0.99 |    0.05 |       - |      - |   4.08 KB |        1.04 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | None            | 524288      | 3,014.76 us |  69.525 us |   201.706 us | 2,978.64 us |  1.00 |    0.09 |       - |      - |    4.4 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | None            | 524288      | 5,149.93 us | 229.374 us |   676.314 us | 4,856.20 us |  1.72 |    0.25 |       - |      - |   4.41 KB |        1.00 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | None            | 524288      |   247.28 us |   4.903 us |     7.634 us |   248.29 us |  1.00 |    0.04 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | None            | 524288      |    44.84 us |   0.888 us |     1.301 us |    44.50 us |  0.18 |    0.01 |  0.1221 |      - |   1.07 KB |        0.86 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | Zstd            | 1024        |    44.09 us |   0.860 us |     2.221 us |    44.34 us |  1.00 |    0.07 |  0.3662 | 0.0610 |   3.25 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 1024        |    45.49 us |   0.880 us |     2.319 us |    45.30 us |  1.03 |    0.07 |  0.3662 |      - |   3.17 KB |        0.98 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | Zstd            | 1024        | 2,136.26 us |  41.022 us |   102.159 us | 2,123.77 us |  1.00 |    0.07 |       - |      - |    6.4 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 1024        | 2,421.36 us |  47.743 us |   110.651 us | 2,439.32 us |  1.14 |    0.08 |       - |      - |   6.37 KB |        1.00 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | Zstd            | 1024        |   162.56 us |   7.321 us |    21.587 us |   152.59 us |  1.02 |    0.18 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 1024        |    46.65 us |   0.926 us |     2.164 us |    46.10 us |  0.29 |    0.04 |  0.1221 |      - |   1.07 KB |        0.86 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | Zstd            | 524288      | 2,321.44 us |  46.317 us |   110.973 us | 2,312.66 us |  1.00 |    0.07 | 11.7188 |      - |  70.33 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 524288      | 2,030.57 us |  40.354 us |   106.310 us | 2,029.19 us |  0.88 |    0.06 | 11.7188 |      - |  70.34 KB |        1.00 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | Zstd            | 524288      | 3,725.80 us | 108.855 us |   320.962 us | 3,762.83 us |  1.01 |    0.13 | 11.7188 |      - |  70.82 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 524288      | 4,999.09 us | 125.247 us |   365.351 us | 5,019.71 us |  1.35 |    0.16 |  7.8125 |      - |  70.79 KB |        1.00 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | Zstd            | 524288      |   131.42 us |   0.803 us |     0.712 us |   131.53 us |  1.00 |    0.01 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | Zstd            | 524288      |    35.77 us |   0.311 us |     0.291 us |    35.79 us |  0.27 |    0.00 |  0.1221 |      - |   1.07 KB |        0.86 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | LZ4             | 1024        |    35.63 us |   0.217 us |     0.203 us |    35.64 us |  1.00 |    0.01 |  0.3662 | 0.1221 |   2.94 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 1024        |    37.37 us |   0.726 us |     0.865 us |    37.60 us |  1.05 |    0.02 |  0.3662 | 0.0610 |    2.9 KB |        0.99 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | LZ4             | 1024        | 1,811.87 us |  38.188 us |   110.791 us | 1,795.18 us |  1.00 |    0.09 |       - |      - |   5.89 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 1024        | 2,183.73 us |  76.578 us |   220.945 us | 2,255.47 us |  1.21 |    0.15 |       - |      - |   4.86 KB |        0.83 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | LZ4             | 1024        |   130.25 us |   1.806 us |     2.591 us |   130.59 us |  1.00 |    0.03 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 1024        |    40.93 us |   1.201 us |     3.542 us |    41.12 us |  0.31 |    0.03 |  0.1221 |      - |   1.07 KB |        0.86 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Writing of new object (I/O Complete)'          | .NET 10.0      | .NET 10.0      | LZ4             | 524288      | 1,545.77 us |  53.391 us |   154.044 us | 1,520.55 us |  1.01 |    0.14 | 11.7188 | 1.9531 |  75.17 KB |        1.00 |
| 'Writing of new object (I/O Complete)'          | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 524288      | 1,696.89 us |  49.925 us |   143.243 us | 1,670.63 us |  1.11 |    0.14 |  7.8125 |      - |   74.1 KB |        0.99 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Overwriting existing object (Update Metadata)' | .NET 10.0      | .NET 10.0      | LZ4             | 524288      | 2,725.77 us |  67.584 us |   197.144 us | 2,716.33 us |  1.01 |    0.10 | 11.7188 |      - |  76.72 KB |        1.00 |
| 'Overwriting existing object (Update Metadata)' | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 524288      | 4,658.20 us | 109.336 us |   317.203 us | 4,679.55 us |  1.72 |    0.17 |  7.8125 |      - |  74.28 KB |        0.97 |
|                                                 |                |                |                 |             |             |            |              |             |       |         |         |        |           |             |
| 'Random read (Simulates web traffic)'           | .NET 10.0      | .NET 10.0      | LZ4             | 524288      |   133.18 us |   2.652 us |     7.694 us |   132.89 us |  1.00 |    0.08 |       - |      - |   1.24 KB |        1.00 |
| 'Random read (Simulates web traffic)'           | NativeAOT 10.0 | NativeAOT 10.0 | LZ4             | 524288      |    35.96 us |   0.506 us |     0.448 us |    36.07 us |  0.27 |    0.02 |  0.1221 |      - |   1.07 KB |        0.86 |

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