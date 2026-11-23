# Java user guide

## Setup

Use JDK 25 and either Maven 3.9 or later or Gradle 9.1 or later. The JAR includes the shared library for Windows, Linux glibc, Linux musl and macOS on x64 and ARM64. Use a JVM with the target architecture. The [native platform table](../BuildFromSource.md#platforms-and-architectures) lists the CI environments.

Add the Maven dependency once the version is available on Maven Central, or [build and install it locally](../BuildFromSource.md#build-the-java-jar).

```xml
<dependency>
  <groupId>io.github.noyzone99</groupId>
  <artifactId>vessel-storage</artifactId>
  <version>0.0.1-alpha.1</version>
</dependency>
```

Gradle Kotlin DSL uses the same coordinates.

```kotlin
repositories { mavenCentral() }
dependencies { implementation("io.github.noyzone99:vessel-storage:0.0.1-alpha.1") }
```

Pass `--enable-native-access=ALL-UNNAMED` to the application JVM for classpath use. On the module path, use `--enable-native-access=io.github.noyzone99.vessel`. The binding picks the bundled binary for the running JVM and loads it automatically. Set `-Dvessel.library.path=/absolute/path/to/libvessel.so` to load a matching external library, for example when a container has a restricted temporary filesystem.

To use the files from a [GitHub Release](https://github.com/noyzone99/Vessel/releases), download the JAR and POM for the same version and install them in your local Maven repository.

```shell
mvn install:install-file -Dfile=vessel-storage-0.0.1-alpha.1.jar -DpomFile=vessel-storage-0.0.1-alpha.1.pom
```

## Write and read an object

Run the [complete example](../../../Examples/java/src/main/java/Example.java) from the repository root.

```shell
mvn -f Examples/java/pom.xml compile exec:exec
gradle -p Examples/java run
```

For a locally installed build, pass `-PvesselLocal` to the Gradle example. It then resolves Vessel from the local Maven repository.

The example opens `my-store`, streams `Hello from Vessel!` into `documents/hello.txt` with overwrite enabled and prints the text after reading it back. Run it again to replace the object at the same key.

## Common tasks

### Stream files

`put` reads an input stream from its current position and returns the committed metadata. Close the source and any stream from `open` with try-with-resources.

```java
try (var store = new VesselStorage(Path.of("vessel-data"));
     var source = Files.newInputStream(Path.of("archive.bin"))) {
    var options = new WriteOptions(true, CompressionType.ZSTD, CompressionLevel.BALANCED,
        "archive", ".bin", "application/octet-stream");
    store.put("backups/archive.bin", source, options);
    try (var stored = store.open("backups/archive.bin")) {
        Files.copy(stored, Path.of("restored.bin"), StandardCopyOption.REPLACE_EXISTING);
    }
}
```

Use `metadata(key)` for a metadata lookup, `keys("backups/")` for prefix listings and `delete(key)` to remove an object. `read(key)` verifies the entire payload and has a 64 MiB limit. `open(key)` supports larger payloads and keeps the selected version readable across overwrites and deletions.

## See also

- [Java API reference](../../ApiReferences/Java/README.md)
- [Kotlin/JVM user guide](../Kotlin/README.md)
- [Shared storage behavior](../Storage.md)
