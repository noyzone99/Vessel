# C++ user guide

## Setup

Use a C++17 compiler and the [header-only binding](../../../Bindings/cpp/include/vessel/vessel.hpp) from a Vessel source checkout. Add `Bindings/cpp/include` and the directory that contains `vessel.h` to your compiler's include paths.

Use the shared library and C header from the same [native release archive](https://github.com/noyzone99/Vessel/releases), [CI artifact](../BuildFromSource.md#platforms-and-architectures) or [source build](../BuildFromSource.md#build-the-shared-library). Match the application's OS, architecture and Linux C library. Windows applications link against `vessel.lib` and require `vessel.dll` at runtime.

## Write and read an object

[The example](../../../Examples/cpp/main.cpp) creates `my-store`, writes the UTF-8 bytes of `Hello from Vessel!` to `documents/hello.txt` and streams the object to standard output. It enables overwrite so you can run it again.

```cpp
#include <iostream>
#include <sstream>
#include <vessel/vessel.hpp>

int main() {
    vessel::Vessel store("my-store");
    const auto* key = "documents/hello.txt";
    std::istringstream input("Hello from Vessel!");
    vessel::PutOptions options;
    options.overwrite = true;
    store.put(key, input, options);
    auto reader = store.open_read(key);
    reader.copy_to(std::cout);
    std::cout << '\n';
}
```

On Linux or macOS, point `native_dir` at the extracted archive and run from the repository root.

```sh
native_dir=/absolute/path/to/extracted-archive
c++ -std=c++17 -I Bindings/cpp/include -I "$native_dir" Examples/cpp/main.cpp \
  -L "$native_dir" -lvessel "-Wl,-rpath,$native_dir" -o vessel-cpp-example
./vessel-cpp-example
```

On Windows, run from the repository root in a Developer PowerShell for the target architecture. For a source build, point `$import` at `vessel.lib` in the sibling `native` directory and add `Sources/Vessel.Native` to the include paths if needed.

```powershell
$nativeDir = 'C:\path\to\extracted-archive'
$import = Join-Path $nativeDir 'vessel.lib'
cl.exe /nologo /std:c++17 /EHsc /I Bindings/cpp/include "/I$nativeDir" Examples/cpp/main.cpp $import /Fe:vessel-cpp-example.exe
$env:PATH = "$nativeDir;$env:PATH"
.\vessel-cpp-example.exe
```

## Common tasks

- Stream a file with `std::ifstream` in binary mode and pass it to `store.put`. Stream it back to a binary `std::ofstream` with `reader.copy_to(output)` or consume chunks with `reader.read(buffer, size)`.
- Set `PutOptions::compression` to `Compression::zstd` or `Compression::lz4`. `PutOptions::level` defaults to `CompressionLevel::balanced`.
- Read object metadata with `store.metadata(key)`. Use `store.keys(prefix)` to find objects and `store.erase(key)` to remove one.

## See also

- [C++ API reference](../../ApiReferences/Cpp/README.md)
- [Shared storage behavior](../Storage.md)
- [C ABI reference](../../ApiReferences/C/README.md)
