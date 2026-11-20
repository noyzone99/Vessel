# Python user guide

## Setup

Python can load Vessel with the standard library's `ctypes` module. Get the shared library from a [matching native CI artifact](../BuildFromSource.md#platforms-and-architectures) or [build it from source](../BuildFromSource.md#build-the-shared-library). Match the Python process's architecture and, on Linux, its C library. An official Python package is planned.

## Write and read an object

Save the following as `hello.py`. It loads the shared library, checks the ABI version, writes an object and copies its payload before freeing the native result.

~~~python
import ctypes as c
from pathlib import Path
import sys


class ObjectInfo(c.Structure):
    _fields_ = [(name, c.c_char_p) for name in ("file_name", "extension", "mime_type")]


class VesselObject(c.Structure):
    _fields_ = [
        ("data", c.c_void_p),
        ("length", c.c_int64),
        ("checksum", c.c_char_p),
        ("file_name", c.c_char_p),
        ("extension", c.c_char_p),
        ("mime_type", c.c_char_p),
    ]


def check(status):
    if status != 0:
        raise RuntimeError(f"Vessel status {status}")


vessel = c.CDLL(str(Path(sys.argv[1]).resolve(strict=True)))
vessel.vessel_abi_version.argtypes = []
vessel.vessel_abi_version.restype = c.c_int32
if vessel.vessel_abi_version() != 1:
    raise RuntimeError("Incompatible Vessel ABI")

vessel.vessel_init.argtypes = [c.c_char_p, c.POINTER(c.c_void_p)]
vessel.vessel_init.restype = c.c_int32
vessel.vessel_put.argtypes = [
    c.c_void_p, c.c_char_p, c.c_void_p, c.c_int32, c.c_int32,
    ObjectInfo, c.c_int32, c.c_int32,
]
vessel.vessel_put.restype = c.c_int32
vessel.vessel_read.argtypes = [c.c_void_p, c.c_char_p, c.POINTER(VesselObject)]
vessel.vessel_read.restype = c.c_int32
vessel.vessel_free_object.argtypes = [c.POINTER(VesselObject)]
vessel.vessel_free_object.restype = None
vessel.vessel_dispose.argtypes = [c.c_void_p]
vessel.vessel_dispose.restype = None

store = c.c_void_p()
result = VesselObject()
key = b"documents/hello.txt"
payload = b"Hello from Vessel!"
info = ObjectInfo(b"hello.txt", b".txt", b"text/plain")
check(vessel.vessel_init(b"my-store", c.byref(store)))
try:
    # Overwrite enabled, no compression, balanced compression level.
    check(vessel.vessel_put(store, key, payload, len(payload), 1, info, 0, 1))
    check(vessel.vessel_read(store, key, c.byref(result)))
    print(c.string_at(result.data, result.length).decode("utf-8"))
finally:
    vessel.vessel_free_object(c.byref(result))
    vessel.vessel_dispose(store)
~~~

Run `python hello.py <path-to-library>` using `vessel.dll`, `vessel.so` or `vessel.dylib` for your platform. The script prints `Hello from Vessel!` and creates `my-store` in the working directory.

## Common tasks

- Encode string keys and file metadata with `.encode("utf-8")`. Pass payload bytes and an explicit length so binary data can contain zeros.
- Use `vessel_stream_open` and `vessel_stream_read` to read large payloads into reusable buffers. Close the handle with `vessel_stream_close`.
- See the [Python smoke test](../../../Tests/Vessel.Native.Tests/smoke.py) for callback uploads, prefix enumeration and deletion.

The [Python API reference](../../ApiReferences/Python/README.md) covers type mappings and ownership. The [C ABI reference](../../ApiReferences/C/README.md) defines the functions, status codes and callback behavior.
