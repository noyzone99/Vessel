# Python user guide

## Setup

Use Python 3.10 or later. For a published alpha, install the wrapper from [PyPI](https://pypi.org/project/vessel-storage/).

~~~sh
python -m pip install vessel-storage==0.0.1a1
~~~

Each wheel includes the native library. When installing from PyPI, pip selects the wheel for the Python process's OS, architecture and, on Linux, its C library. See the [release platform matrix](../BuildFromSource.md#platforms-and-architectures).

To install from [GitHub Releases](https://github.com/noyzone99/Vessel/releases), download the matching wheel. For Windows x64, run `python -m pip install ./vessel_storage-0.0.1a1-py3-none-win_amd64.whl`.

To develop from a checkout, install with `python -m pip install ./Bindings/python` and use `library_path` or `VESSEL_LIBRARY_PATH` to select a [native library built from source](../BuildFromSource.md#build-the-shared-library). These settings can also override the bundled library.

To package both together, [build a wheel from source](../BuildFromSource.md#build-a-python-wheel).

## Write and read an object

Save this as `hello.py`. It creates `my-store` in the working directory.

~~~python
from io import BytesIO
from vessel import VesselStorage

with VesselStorage("my-store") as store:
    with BytesIO(b"Hello from Vessel!") as source:
        metadata = store.put_stream("documents/hello.txt", source, overwrite=True)
    with store.open(metadata.key) as content:
        print(content.read().decode("utf-8"))
~~~

Run `python hello.py`. The program prints `Hello from Vessel!`. The overwrite option lets you rerun it with the same store and key.

## Common tasks

### Stream files

Use `put_stream()` to upload from a binary file and `open()` to download in chunks. This example assumes `input.bin` exists in the working directory.

~~~python
from shutil import copyfileobj

from vessel import CompressionType, VesselStorage

with VesselStorage("my-store") as store:
    with open("input.bin", "rb") as source:
        metadata = store.put_stream(
            "archive/input.bin", source, overwrite=True, compression=CompressionType.ZSTD
        )

    with store.open(metadata.key) as source, open("output.bin", "wb") as destination:
        copyfileobj(source, destination)
~~~

Uploads start at the input's current position. Use `read()` for objects up to 64 MiB and a stream for larger objects. Uncompressed streams support seeking. An open stream keeps its version if another operation overwrites or deletes the key.

### List, inspect and delete objects

- Use `store.keys("documents/")` to collect matching keys. An empty prefix collects all keys.
- Use `store.get_metadata(key)` to inspect sizes, compression, hashes and file attributes without reading the payload.
- Use `store.delete(key)` to remove an object. It returns `True` if it removed an object and `False` if the key was absent.

## See also

- [Python API reference](../../ApiReferences/Python/README.md)
- [Shared storage behavior](../Storage.md)
