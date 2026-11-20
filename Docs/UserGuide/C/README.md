# C user guide

## Setup

Get `vessel.h` and a shared library for your target from the same build. Use a [native CI artifact](../BuildFromSource.md#platforms-and-architectures) or [publish from source](../BuildFromSource.md#build-the-shared-library). Match the host process's OS, architecture and Linux C library.

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

On Windows, open a Developer PowerShell for the target architecture. Set `$publish` and `$import` to your build paths. If you downloaded a CI artifact, use its copy of `vessel.lib`.

~~~powershell
$publish = 'C:\path\to\publish'
$import = 'C:\path\to\native\vessel.lib'
cl.exe /nologo /std:c11 "/I$publish" hello.c $import /Fe:hello.exe
$env:PATH = "$publish;$env:PATH"
.\hello.exe
~~~

On Linux or macOS, use the absolute path to the publish directory. Set the library filename to `vessel.dylib` on macOS.

~~~sh
publish=/absolute/path/to/publish
library=vessel.so
clang -std=c11 -I "$publish" hello.c "$publish/$library" "-Wl,-rpath,$publish" -o hello
./hello
~~~

## Common tasks

- Read or write large objects with `vessel_stream_open` and `vessel_stream_read`, or `vessel_put_stream` for uploads. `vessel_read` has a 64 MiB limit.
- Get metadata without reading the payload with `vessel_get_metadata_ex`. Free the embedded metadata with `vessel_free_metadata`.
- List or remove keys with `vessel_get_all_keys_ex` and `vessel_delete`.

See the [C ABI reference](../../ApiReferences/C/README.md) for status codes, callbacks and ownership. The [C smoke test](../../../Tests/Vessel.Native.Tests/smoke.c) includes a streaming example.
