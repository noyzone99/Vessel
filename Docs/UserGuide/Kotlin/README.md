# Kotlin/JVM user guide

## Setup

Use JDK 25. The [example Gradle project](../../../Examples/kotlin/) uses the Kotlin/JVM 2.4.0 plugin and Gradle 9.3.1. It consumes the same JAR as Java, including the bundled native binaries. See the [native platform table](../BuildFromSource.md#platforms-and-architectures) for supported targets.

Use these coordinates for a published version or [build and install the JAR locally](../BuildFromSource.md#build-the-java-jar).

```kotlin
repositories { mavenCentral() }
dependencies { implementation("io.github.noyzone99:vessel-storage:0.0.1-alpha.1") }
```

Pass `--enable-native-access=ALL-UNNAMED` to the application JVM when using the classpath. The example configures it for Gradle's `run` task. See the [Java setup guide](../Java/README.md#setup) for module-path use and overriding the native library.

## Write and read an object

Run the [example source](../../../Examples/kotlin/src/main/kotlin/Example.kt) from the repository root.

```sh
gradle -p Examples/kotlin run
```

Add `-PvesselLocal` when using a JAR installed in the local Maven repository. The example opens `my-store`, writes `documents/hello.txt` with overwrite enabled and prints `Hello from Vessel!` after reading it back. It uses `use` to close the store and both streams.

## Common tasks

| Task | API |
| --- | --- |
| Stream a file | Pass `Files.newInputStream(path)` to `store.put`, and read through `store.open(key)`. Close both streams with `use`. |
| Attach file metadata | Supply the file name, extension and MIME type in `WriteOptions`. |
| Compress a payload | Set `CompressionType.LZ4` or `CompressionType.ZSTD` in `WriteOptions`. |
| Inspect metadata | Call `store.metadata(key)`. It returns `null` for a missing key. |
| List related keys | Call `store.keys("documents/")`. |
| Delete an object | Call `store.delete(key)`. The result indicates whether the key existed. |

## See also

- [Kotlin/JVM API reference](../../ApiReferences/Kotlin/README.md)
- [Shared storage behavior](../Storage.md)
