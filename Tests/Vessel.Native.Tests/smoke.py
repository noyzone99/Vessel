"""Smoke and recovery tests for a published Vessel shared library."""
import ctypes as c
import hashlib
import os
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import subprocess
import sys
import tempfile
import threading


class ObjectInfo(c.Structure):
    _fields_ = [(name, c.c_char_p) for name in ("file_name", "extension", "mime_type")]


class Object(c.Structure):
    _fields_ = [("data", c.c_void_p), ("length", c.c_int64), ("checksum", c.c_char_p),
                ("file_name", c.c_char_p), ("extension", c.c_char_p), ("mime_type", c.c_char_p)]


ReadCallback = c.CFUNCTYPE(c.c_int32, c.c_void_p, c.c_void_p, c.c_int32)
KeyCallback = c.CFUNCTYPE(c.c_int32, c.c_void_p, c.c_char_p)


class Api:
    def __init__(self, binary):
        self.lib = c.CDLL(str(binary))
        pointer = c.POINTER
        self.bind("vessel_abi_version", c.c_int32)
        self.bind("vessel_init", c.c_int32, c.c_char_p, pointer(c.c_void_p))
        self.bind("vessel_dispose", None, c.c_void_p)
        self.bind("vessel_put", c.c_int32, c.c_void_p, c.c_char_p, c.c_void_p,
                  c.c_int32, c.c_int32, ObjectInfo, c.c_int32, c.c_int32)
        self.bind("vessel_put_stream", c.c_int32, c.c_void_p, c.c_char_p, ReadCallback, c.c_void_p,
                  c.c_int32, ObjectInfo, c.c_int32, c.c_int32, c.c_void_p)
        self.bind("vessel_read", c.c_int32, c.c_void_p, c.c_char_p, pointer(Object))
        self.bind("vessel_delete", c.c_int32, c.c_void_p, c.c_char_p, pointer(c.c_int32))
        self.bind("vessel_get_all_keys_ex", c.c_int32, c.c_void_p, c.c_char_p, KeyCallback, c.c_void_p)
        self.bind("vessel_free_object", None, pointer(Object))

    def bind(self, name, result, *arguments):
        function = getattr(self.lib, name)
        function.restype = result
        function.argtypes = arguments

    def open(self, path):
        handle = c.c_void_p()
        check(self.lib.vessel_init(str(path).encode(), c.byref(handle)))
        return handle

    def put(self, handle, key, payload, overwrite=False, codec=0):
        check(self.lib.vessel_put(handle, key, payload, len(payload), overwrite, ObjectInfo(), codec, 1))

    def read(self, handle, key):
        result = Object()
        check(self.lib.vessel_read(handle, key, c.byref(result)))
        try:
            payload = c.string_at(result.data, result.length)
            assert result.checksum == hashlib.sha256(payload).hexdigest().encode()
            return payload
        finally:
            self.lib.vessel_free_object(c.byref(result))

    def keys(self, handle, prefix=b""):
        keys = []

        @KeyCallback
        def collect(_context, key):
            keys.append(key)
            return 0

        check(self.lib.vessel_get_all_keys_ex(handle, prefix, collect, None))
        return keys


def check(status):
    if status != 0:
        raise RuntimeError(f"Vessel status {status}")


def write_before_crash(binary, root):
    api = Api(binary)
    handle = api.open(root)
    api.put(handle, b"state", b"previous", codec=1)
    api.put(handle, b"state", b"committed", overwrite=True, codec=2)
    api.put(handle, b"deleted", b"must stay deleted")
    deleted = c.c_int32()
    check(api.lib.vessel_delete(handle, b"deleted", c.byref(deleted)))
    assert deleted.value == 1
    os._exit(0)


def check_crash_recovery(api, binary, root):
    subprocess.run([sys.executable, __file__, str(binary), "--crash-writer", str(root)], check=True, timeout=30)
    handle = api.open(root)
    try:
        assert api.keys(handle) == [b"state"]
        assert api.read(handle, b"state") == b"committed"
    finally:
        api.lib.vessel_dispose(handle)


def check_lifetime(api, root):
    handle = api.open(root)
    entered = threading.Event()
    release = threading.Event()
    callback_threads = []
    payload = b"streaming client data"
    worker_thread = None

    @ReadCallback
    def supply(_context, output, capacity):
        callback_threads.append(threading.get_ident())
        if entered.is_set():
            return 0
        entered.set()
        if not release.wait(10) or capacity < len(payload):
            return -5
        c.memmove(output, payload, len(payload))
        return len(payload)

    def upload():
        nonlocal worker_thread
        worker_thread = threading.get_ident()
        return api.lib.vessel_put_stream(handle, b"streamed", supply, None, 0, ObjectInfo(), 1, 1, None)

    with ThreadPoolExecutor(max_workers=1) as pool:
        future = pool.submit(upload)
        try:
            assert entered.wait(10), "The read callback was never called"
            api.lib.vessel_dispose(handle)
            result = Object()
            assert api.lib.vessel_read(handle, b"streamed", c.byref(result)) == -6
            assert not result.data and result.length == 0
            second = c.c_void_p()
            assert api.lib.vessel_init(str(root).encode(), c.byref(second)) == -5
        finally:
            release.set()
        check(future.result(timeout=10))

    assert callback_threads == [worker_thread, worker_thread]
    reopened = api.open(root)
    try:
        assert api.read(reopened, b"streamed") == payload
        for codec in range(3):
            key = f"  é/{codec}".encode()
            api.put(reopened, key, b"", codec=codec)
            assert api.read(reopened, key) == b""
        assert api.keys(reopened, b"  ") == [f"  é/{codec}".encode() for codec in range(3)]
    finally:
        api.lib.vessel_dispose(reopened)


if __name__ == "__main__":
    library = Path(sys.argv[1]).resolve(strict=True)
    if len(sys.argv) == 4 and sys.argv[2] == "--crash-writer":
        write_before_crash(library, Path(sys.argv[3]))

    api = Api(library)
    assert api.lib.vessel_abi_version() == 1
    with tempfile.TemporaryDirectory(prefix="vessel-native-tests-") as directory:
        root = Path(directory)
        check_lifetime(api, root / "lifetime")
        check_crash_recovery(api, library, root / "crash")
    print("Vessel Python ABI: callback thread, close during upload, UTF-8, empty objects and crash recovery passed.")
