# C user guide

## Setup

Download the `vessel-native-dynamic-<platform>-<arch>-<version>.zip` archive for your target from [GitHub Releases](https://github.com/noyzone99/Vessel/releases). Linux archive names include `glibc` or `musl` before the architecture. The archive contains `vessel.h`, the shared library and the license. Windows archives also contain the `vessel.lib` import library. Experimental static archives are available for Windows x64 and Linux glibc x64.

Before a release is published, use a [native CI artifact](../BuildFromSource.md#platforms-and-architectures) or [publish from source](../BuildFromSource.md#build-the-shared-library). Use the header and library from the same build. Match the process's OS, architecture and Linux C library.

## Write and read an object

Save this as `hello.c`. The program opens `my-store`, writes `documents/hello.txt` with overwrite enabled and streams it back. It prints `Hello from Vessel!`.

~~~c
#include <stdio.h>
#include "vessel.h"

int main(void)
{
    if (vessel_abi_version() != VESSEL_ABI_VERSION) {
        fputs("Incompatible Vessel ABI\n", stderr);
        return 1;
    }

    const char *key = "documents/hello.txt";
    const uint8_t payload[] = "Hello from Vessel!";
    vessel_object_info_t info = {0};
    vessel_t store = NULL;
    vessel_stream_t content = NULL;

    vessel_error_t status = vessel_init("my-store", &store);
    if (status == VESSEL_SUCCESS) {
        status = vessel_put_ex(store, key, payload, sizeof(payload) - 1, 1,
            info, VESSEL_COMPRESSION_NONE, VESSEL_LEVEL_BALANCED, NULL);
    }
    if (status == VESSEL_SUCCESS) {
        status = vessel_stream_open(store, key, &content);
    }

    uint8_t buffer[256];
    int32_t count;
    while (status == VESSEL_SUCCESS) {
        status = vessel_stream_read(content, buffer, sizeof(buffer), &count);
        if (status != VESSEL_SUCCESS || count == 0) {
            break;
        }
        if (fwrite(buffer, 1, (size_t)count, stdout) != (size_t)count) {
            status = VESSEL_ERR_IO;
        }
    }
    if (status == VESSEL_SUCCESS) {
        putchar('\n');
    } else {
        fprintf(stderr, "Vessel status %d\n", (int)status);
    }

    vessel_stream_close(content);
    vessel_dispose(store);
    return status == VESSEL_SUCCESS ? 0 : 1;
}
~~~

On Windows, open a Developer PowerShell for the target architecture. Set `$publish` to the extracted archive directory. When building from source, point `$import` at `vessel.lib` in the sibling `native/` directory.

~~~powershell
$publish = 'C:\path\to\vessel-native-dynamic-windows-x64-0.0.1-alpha.1'
$import = Join-Path $publish 'vessel.lib'
cl.exe /nologo /std:c11 "/I$publish" hello.c $import /Fe:hello.exe
$env:PATH = "$publish;$env:PATH"
.\hello.exe
~~~

On Linux or macOS, set `publish` to the absolute path of the extracted archive or publish directory. Clang resolves `-lvessel` to `libvessel.so` on Linux and `libvessel.dylib` on macOS.

~~~sh
publish=/absolute/path/to/extracted-archive
clang -std=c11 -I "$publish" hello.c -L "$publish" -lvessel "-Wl,-rpath,$publish" -o hello
./hello
~~~

### Link an experimental static library

Choose a `vessel-native-static-<platform>-<arch>-<version>.zip` archive. It contains `vessel.h`, the static library, `vessel-link.rsp` with the platform linker options and the license notices. Set the package path to the extracted directory and use the same `hello.c` example.

On Windows x64, run this in an x64 Developer PowerShell.

~~~powershell
$package = 'C:\path\to\vessel-native-static-windows-x64-0.0.1-alpha.1'
$archive = Join-Path $package 'vessel_static.lib'
$options = Join-Path $package 'vessel-link.rsp'
cl.exe /nologo /c /MT /std:c11 "/I$package" hello.c /Fo:hello.obj
link.exe /nologo /OUT:hello.exe hello.obj $archive "@$options"
.\hello.exe
~~~

On Linux glibc x64, use Clang and GNU binutils.

~~~sh
package=/absolute/path/to/vessel-native-static-linux-glibc-x64-0.0.1-alpha.1
clang -std=c11 -I "$package" hello.c "$package/libvessel.a" "@$package/vessel-link.rsp" -o hello
./hello
~~~

The executable contains Vessel and its NativeAOT runtime code. The linker options still use platform libraries. NativeAOT supports one statically linked NativeAOT library per loadable module. [Microsoft static library example](https://github.com/dotnet/samples/tree/main/core/nativeaot/NativeLibrary#building-static-libraries)

## Common tasks

- Read or write large objects with `vessel_stream_open` and `vessel_stream_read`, or `vessel_put_stream` for uploads. `vessel_read` has a 64 MiB limit.
- Get metadata without reading the payload with `vessel_get_metadata_ex`. Free the embedded metadata with `vessel_free_metadata`.
- List or remove keys with `vessel_get_all_keys_ex` and `vessel_delete`.

## See also

- [C ABI reference](../../ApiReferences/C/README.md)
- [Shared storage behavior](../Storage.md)
- [Streaming upload in the C smoke test](../../../Tests/Vessel.Native.Tests/smoke.c)
