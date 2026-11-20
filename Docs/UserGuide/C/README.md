# C user guide

## Setup

Download the `vessel-native-<platform>-<arch>-<version>.zip` archive for your target from [GitHub Releases](https://github.com/noyzone99/Vessel/releases). Linux archive names include `glibc` or `musl` before the architecture. Extract the archive to get `vessel.h`, the shared library and the license. Windows archives also contain the `vessel.lib` import library.

Before a release is published, use a [native CI artifact](../BuildFromSource.md#platforms-and-architectures) or [publish from source](../BuildFromSource.md#build-the-shared-library). Use the header and library from the same build. Match the process's OS, architecture and Linux C library.

## Write and read an object

Save this as `hello.c`. The program checks the ABI version, writes a small object, reads it back and releases the resources allocated by Vessel.

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
    vessel_object_info_t info = {"hello.txt", ".txt", "text/plain"};
    vessel_t store = NULL;
    vessel_object_t object = {0};

    vessel_error_t status = vessel_init("my-store", &store);
    if (status == VESSEL_SUCCESS) {
        status = vessel_put_ex(store, key, payload, sizeof(payload) - 1, 1,
            info, VESSEL_COMPRESSION_NONE, VESSEL_LEVEL_BALANCED, NULL);
    }
    if (status == VESSEL_SUCCESS) {
        status = vessel_read(store, key, &object);
    }

    if (status == VESSEL_SUCCESS) {
        fwrite(object.data, 1, (size_t)object.length, stdout);
        putchar('\n');
    } else {
        fprintf(stderr, "Vessel status %d\n", (int)status);
    }

    vessel_free_object(&object);
    vessel_dispose(store);
    return status == VESSEL_SUCCESS ? 0 : 1;
}
~~~

On Windows, open a Developer PowerShell for the target architecture. Set `$publish` to the extracted archive directory. When building from source, point `$import` at `vessel.lib` in the sibling `native/` directory.

~~~powershell
$publish = 'C:\path\to\vessel-native-windows-x64-0.0.1-alpha.1'
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

## Common tasks

- Read or write large objects with `vessel_stream_open` and `vessel_stream_read`, or `vessel_put_stream` for uploads. `vessel_read` has a 64 MiB limit.
- Get metadata without reading the payload with `vessel_get_metadata_ex`. Free the embedded metadata with `vessel_free_metadata`.
- List or remove keys with `vessel_get_all_keys_ex` and `vessel_delete`.

See the [C ABI reference](../../ApiReferences/C/README.md) for status codes, callbacks and ownership. The [C smoke test](../../../Tests/Vessel.Native.Tests/smoke.c) includes a streaming example.
