# Vessel repository guidance

## Project direction

Vessel is an embedded object store for application-owned local filesystems. Keep the engine small enough to embed in an application and expose the same storage semantics through the managed API and the native C ABI.

- **Performance.** Keep reads and writes stream-oriented. Avoid unnecessary allocations, copies, whole-object buffering and lock contention on hot paths. Measure changes with representative payloads, codecs and concurrency. Record the runtime, platform and workload when reporting benchmarks.
- **Small footprint.** Keep the core in process and minimize dependencies, startup work and memory use. Keep language and application-framework integration outside the storage engine.
- **Multilingual access.** Keep `Vessel.Core` idiomatic for .NET and NativeAOT-compatible. Treat `Sources/Vessel.Native/vessel.h` as the public interop contract. Preserve compatible signatures, struct layouts, status codes and ownership rules within an ABI version. Reuse the same engine when introducing language bindings.
- **Storage reliability.** Preserve committed data across supported reopen and recovery scenarios. Check that performance changes preserve cancellation, overwrite and delete behavior, reader lifetimes and compatibility with existing stores.

## Targets

| Interface | Target |
| --- | --- |
| Managed `Vessel.Core` | .NET 8 and .NET 10 |
| Native `Vessel.Native` | .NET 10 NativeAOT shared library with a C ABI |
| Native platforms | Windows, Linux (glibc and musl) and macOS, each on x64 and ARM64 |
| Language integration | C# through `Vessel.Core`, C through `vessel.h`, and Python through `vessel-storage` |

Use the project files and [validation workflow](.github/workflows/validate.yml) to verify framework targets and the native CI matrix. Java and Node.js bindings are planned. Describe roadmap items according to their implementation and validation status.

## Documentation layout

Write repository documentation and examples in English. Use Markdown, relative links and case-sensitive paths that work on GitHub and Linux.

```text
README.md                          Project overview, quick start, source build
Docs/
  README.md                        Documentation index
  UserGuide/
    README.md                      Guide index and language status
    BuildFromSource.md             Builds, native targets and validation
    Storage.md                     Shared storage behavior and operations
    <Language>/README.md           Setup, first example and common tasks
  ApiReferences/
    README.md                      API index
    <Language>/README.md           Public contracts and interop details
```

- Use `CSharp`, `C` and `Python` as the current language directory names. Add matching guide and reference directories when a new integration has a verifiable example and public API or interop contract. Add focused topic pages as needed and link them from the relevant `README.md`.
- Start each language guide with prerequisites and setup, followed by a runnable write-and-read example, common tasks and links to its API reference and shared topics.
- Organize each reference by public type or operation. Cover parameters and defaults, results and failures, resource ownership, concurrency and version or platform constraints where applicable. Document the native contract in the C reference and link to it from FFI references for shared behavior.
- Keep the root README focused on what Vessel does, why it exists, a short example and how to build from source. Keep the NuGet package README in `Sources/Vessel.Core/Assets/README.md` because NuGet embeds it in the package.
- Update the guides and references when changing public behavior, supported targets or build commands. Verify examples against the current source and tests. Keep the documentation indexes and cross-links current.
