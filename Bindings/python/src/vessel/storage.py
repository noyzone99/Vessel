from __future__ import annotations

import ctypes as c
import io
import os
from operator import index
from typing import BinaryIO
from weakref import finalize

from ._native import (
    CANCELLED,
    INVALID_OPERATION,
    KeyCallback,
    NativeLibrary,
    NativeObject,
    NativeObjectInfo,
    ReadCallback,
    check,
    encode,
    load_library,
)
from .types import CompressionLevel, CompressionType, ObjectInfo, ObjectMetadata, VesselError


class VesselStorage:
    """An open Vessel store on the local filesystem."""

    def __init__(
        self,
        path: str | os.PathLike[str],
        *,
        library_path: str | os.PathLike[str] | None = None,
    ) -> None:
        store_path = encode(os.fspath(path))
        self._native = load_library(library_path)
        handle = c.c_void_p()
        check(self._native.dll.vessel_init(store_path, c.byref(handle)))
        self._handle = handle.value
        self._dispose = finalize(self, self._native.dll.vessel_dispose, self._handle)

    @property
    def closed(self) -> bool:
        return not self._dispose.alive

    def close(self) -> None:
        self._dispose()

    def __enter__(self) -> VesselStorage:
        self._require_handle()
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self.close()

    def _require_handle(self) -> int:
        if self.closed:
            raise ValueError("I/O operation on a closed Vessel store")
        return self._handle

    def put(
        self,
        key: str,
        data: bytes,
        *,
        overwrite: bool = False,
        info: ObjectInfo | None = None,
        compression: CompressionType = CompressionType.NONE,
        level: CompressionLevel = CompressionLevel.BALANCED,
    ) -> ObjectMetadata:
        """Store a bytes object and return its metadata."""
        if not isinstance(data, bytes):
            raise TypeError("data must be bytes")
        return self._native.read_metadata(
            self._native.dll.vessel_put_ex, self._require_handle(), encode(key),
            data, bytes.__len__(data), bool(overwrite), NativeObjectInfo.from_info(info),
            CompressionType(compression), CompressionLevel(level),
        )

    def put_stream(
        self,
        key: str,
        source: BinaryIO,
        *,
        overwrite: bool = False,
        info: ObjectInfo | None = None,
        compression: CompressionType = CompressionType.NONE,
        level: CompressionLevel = CompressionLevel.BALANCED,
    ) -> ObjectMetadata:
        """Store a binary stream from its current position through EOF."""
        read = source.read
        failure = None

        @ReadCallback
        def supply(_context, buffer, capacity):
            nonlocal failure
            try:
                data = read(capacity)
                if not isinstance(data, bytes):
                    raise TypeError("source.read() must return bytes")
                count = bytes.__len__(data)
                if count > capacity:
                    raise ValueError("source.read() returned more bytes than requested")
                if count:
                    c.memmove(buffer, data, count)
                return count
            except BaseException as error:
                # Let the native write roll back before raising in Python.
                failure = error
                return CANCELLED

        try:
            return self._native.read_metadata(
                self._native.dll.vessel_put_stream, self._require_handle(), encode(key),
                supply, None, bool(overwrite), NativeObjectInfo.from_info(info),
                CompressionType(compression), CompressionLevel(level),
            )
        except VesselError:
            if failure is not None:
                raise failure from None
            raise

    def read(self, key: str) -> bytes:
        """Read and verify an entire object up to 64 MiB."""
        result = NativeObject()
        try:
            check(self._native.dll.vessel_read(self._require_handle(), encode(key), c.byref(result)))
            return c.string_at(result.data, result.length) if result.length else b""
        finally:
            self._native.dll.vessel_free_object(c.byref(result))

    def open(self, key: str) -> VesselStream:
        """Open a read-only stream over the current version of an object."""
        handle = c.c_void_p()
        check(self._native.dll.vessel_stream_open(self._require_handle(), encode(key), c.byref(handle)))
        return VesselStream(self._native, handle.value)

    def get_metadata(self, key: str) -> ObjectMetadata:
        return self._native.read_metadata(
            self._native.dll.vessel_get_metadata_ex, self._require_handle(), encode(key),
        )

    def keys(self, prefix: str = "") -> list[str]:
        """Collect the keys that start with prefix."""
        result = []
        failure = None

        @KeyCallback
        def collect(_context, key):
            nonlocal failure
            try:
                result.append(key.decode("utf-8"))
                return 0
            except BaseException as error:
                failure = error
                return CANCELLED

        status = self._native.dll.vessel_get_all_keys_ex(
            self._require_handle(), encode(prefix), collect, None,
        )
        if failure is not None:
            raise failure
        check(status)
        return result

    def delete(self, key: str) -> bool:
        deleted = c.c_int32()
        check(self._native.dll.vessel_delete(self._require_handle(), encode(key), c.byref(deleted)))
        return bool(deleted.value)


class VesselStream(io.RawIOBase):
    """An independent, read-only snapshot of a Vessel object."""

    def __init__(self, native: NativeLibrary, handle: int) -> None:
        super().__init__()
        self._native = native
        self._handle = handle

    def _require_handle(self) -> int:
        if self.closed:
            raise ValueError("I/O operation on a closed Vessel stream")
        return self._handle

    def close(self) -> None:
        if not self.closed:
            try:
                super().close()
            finally:
                self._native.dll.vessel_stream_close(self._handle)

    def readable(self) -> bool:
        self._require_handle()
        return True

    def seekable(self) -> bool:
        result = c.c_int32()
        check(self._native.dll.vessel_stream_can_seek(self._require_handle(), c.byref(result)))
        return bool(result.value)

    @property
    def length(self) -> int:
        result = c.c_int64()
        check(self._native.dll.vessel_stream_length(self._require_handle(), c.byref(result)))
        return result.value

    @property
    def metadata(self) -> ObjectMetadata:
        return self._native.read_metadata(
            self._native.dll.vessel_stream_get_metadata, self._require_handle(),
        )

    def readinto(self, buffer) -> int:
        handle = self._require_handle()
        with memoryview(buffer).cast("B") as view:
            if view.readonly:
                raise TypeError("readinto() requires a writable buffer")
            count = min(view.nbytes, (1 << 31) - 1)
            if not count:
                return 0
            result = c.c_int32()
            check(self._native.dll.vessel_stream_read(
                handle, c.byref(c.c_ubyte.from_buffer(view)), count, c.byref(result),
            ))
            return result.value

    def tell(self) -> int:
        result = c.c_int64()
        status = self._native.dll.vessel_stream_position(self._require_handle(), c.byref(result))
        if status == INVALID_OPERATION:
            raise io.UnsupportedOperation("This Vessel stream does not support seeking")
        check(status)
        return result.value

    def seek(self, offset: int, whence: int = os.SEEK_SET) -> int:
        handle = self._require_handle()
        offset = index(offset)
        whence = index(whence)
        if not -(1 << 63) <= offset < (1 << 63):
            raise OverflowError("offset does not fit in a signed 64-bit integer")
        if whence not in (os.SEEK_SET, os.SEEK_CUR, os.SEEK_END):
            raise ValueError("invalid whence")
        status = self._native.dll.vessel_stream_seek(handle, offset, whence)
        if status == INVALID_OPERATION:
            raise io.UnsupportedOperation("This Vessel stream does not support seeking")
        check(status)
        return self.tell()
