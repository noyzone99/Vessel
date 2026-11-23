# Kotlin/JVM API reference

The [Java API reference](../Java/README.md) defines the types, parameters, defaults, errors and concurrency rules for the `vessel-storage` JAR on JDK 25.

| Type or operation | Use from Kotlin/JVM |
| --- | --- |
| `VesselStorage` | Construct it with `VesselStorage(path)` and close it with `use`. |
| `WriteOptions` | Use the Java record constructor with positional arguments. Use `null` for omitted file attributes. |
| `put` and `open` | `put` consumes a Java `InputStream` and leaves closing it to the caller. Close the `VesselInputStream` returned by `open` with `use`. |
| `metadata`, `keys` and `delete` | Treat `metadata` as nullable when a key is absent. `keys` returns a list and `delete` reports whether it removed a key. |

See the [Kotlin/JVM user guide](../../UserGuide/Kotlin/README.md) for setup and a runnable example.
