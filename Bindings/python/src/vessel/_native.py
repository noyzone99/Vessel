import ctypes as c
from datetime import datetime, timedelta, timezone
from functools import cache
from os import PathLike, environ
from pathlib import Path
from sys import platform

from .types import (
    CompressionType,
    ObjectAlreadyExistsError,
    ObjectInfo,
    ObjectMetadata,
    ObjectNotFoundError,
    VesselError,
    VesselLibraryError,
)

ABI_VERSION = 1
CANCELLED = -8
INVALID_OPERATION = -7
_EPOCH = datetime(1970, 1, 1, tzinfo=timezone.utc)

_STATUS = {
    -1: (VesselError, "Unknown native error"),
    -2: (VesselError, "Invalid argument"),
    -3: (ObjectNotFoundError, "Object not found"),
    -4: (ObjectAlreadyExistsError, "Object already exists"),
    -5: (VesselError, "I/O error"),
    -6: (VesselError, "Native handle is closed"),
    -7: (VesselError, "Invalid operation"),
    -8: (VesselError, "Operation cancelled"),
    -9: (VesselError, "Object exceeds the whole-read limit"),
    -10: (VesselError, "Native allocation failed"),
}


def check(status: int) -> None:
    if status != 0:
        error, message = _STATUS.get(status, (VesselError, "Unexpected native status"))
        raise error(status, message)


def encode(text: str) -> bytes:
    encoded = str.encode(text, "utf-8")
    if b"\0" in encoded:
        raise ValueError("Vessel strings cannot contain NUL")
    return encoded


def _decode_optional(text: bytes | None) -> str | None:
    return text.decode("utf-8") if text is not None else None


class NativeObjectInfo(c.Structure):
    _fields_ = [(name, c.c_char_p) for name in ("file_name", "extension", "mime_type")]

    @classmethod
    def from_info(cls, info: ObjectInfo | None) -> "NativeObjectInfo":
        if info is None:
            return cls()
        fields = (info.file_name, info.extension, info.mime_type)
        return cls(*(encode(text) if text is not None else None for text in fields))


class _Metadata(c.Structure):
    _fields_ = [
        ("original_size", c.c_int64),
        ("compressed_size", c.c_int64),
        ("compression_type", c.c_int32),
        ("creation_date_unix", c.c_int64),
        ("key", c.c_char_p),
        ("hash", c.c_char_p),
        ("etag", c.c_char_p),
        ("file_name", c.c_char_p),
        ("extension", c.c_char_p),
        ("mime_type", c.c_char_p),
    ]


class _MetadataResult(c.Structure):
    _fields_ = [("metadata", _Metadata), ("creation_date_unix_ms", c.c_int64)]


class NativeObject(c.Structure):
    _fields_ = [
        ("data", c.c_void_p),
        ("length", c.c_int64),
        ("checksum", c.c_char_p),
        ("file_name", c.c_char_p),
        ("extension", c.c_char_p),
        ("mime_type", c.c_char_p),
    ]


ReadCallback = c.CFUNCTYPE(c.c_int32, c.c_void_p, c.c_void_p, c.c_int32)
KeyCallback = c.CFUNCTYPE(c.c_int32, c.c_void_p, c.c_char_p)


class NativeLibrary:
    def __init__(self, path: Path) -> None:
        try:
            self.dll = c.CDLL(str(path))
            self._bind("vessel_abi_version", c.c_int32)
            version = self.dll.vessel_abi_version()
            if version != ABI_VERSION:
                raise VesselLibraryError(f"{path} uses Vessel ABI {version}, expected {ABI_VERSION}")
            self._bind_operations()
        except (OSError, AttributeError) as error:
            raise VesselLibraryError(f"Cannot load the Vessel shared library at {path}: {error}") from error

    def _bind(self, name, result, *arguments) -> None:
        function = getattr(self.dll, name)
        function.restype = result
        function.argtypes = arguments

    def _bind_operations(self) -> None:
        handle, text = c.c_void_p, c.c_char_p
        i32, i64, pointer = c.c_int32, c.c_int64, c.POINTER
        metadata = pointer(_MetadataResult)
        self._bind("vessel_init", i32, text, pointer(handle))
        self._bind("vessel_dispose", None, handle)
        self._bind("vessel_put_ex", i32, handle, text, c.c_void_p, i64, i32,
                   NativeObjectInfo, i32, i32, metadata)
        self._bind("vessel_put_stream", i32, handle, text, ReadCallback, c.c_void_p, i32,
                   NativeObjectInfo, i32, i32, metadata)
        self._bind("vessel_read", i32, handle, text, pointer(NativeObject))
        self._bind("vessel_free_object", None, pointer(NativeObject))
        self._bind("vessel_get_metadata_ex", i32, handle, text, metadata)
        self._bind("vessel_free_metadata", None, pointer(_Metadata))
        self._bind("vessel_delete", i32, handle, text, pointer(i32))
        self._bind("vessel_get_all_keys_ex", i32, handle, text, KeyCallback, c.c_void_p)
        self._bind("vessel_stream_open", i32, handle, text, pointer(handle))
        self._bind("vessel_stream_read", i32, handle, c.c_void_p, i32, pointer(i32))
        self._bind("vessel_stream_length", i32, handle, pointer(i64))
        self._bind("vessel_stream_get_metadata", i32, handle, metadata)
        self._bind("vessel_stream_can_seek", i32, handle, pointer(i32))
        self._bind("vessel_stream_position", i32, handle, pointer(i64))
        self._bind("vessel_stream_seek", i32, handle, i64, i32)
        self._bind("vessel_stream_close", None, handle)

    def read_metadata(self, function, *arguments) -> ObjectMetadata:
        result = _MetadataResult()
        try:
            check(function(*arguments, c.byref(result)))
            native = result.metadata
            return ObjectMetadata(
                key=native.key.decode("utf-8"),
                hash=native.hash.decode("utf-8"),
                etag=native.etag.decode("utf-8"),
                original_size=native.original_size,
                compressed_size=native.compressed_size,
                compression_type=CompressionType(native.compression_type),
                creation_date=_EPOCH + timedelta(milliseconds=result.creation_date_unix_ms),
                file_name=_decode_optional(native.file_name),
                extension=_decode_optional(native.extension),
                mime_type=_decode_optional(native.mime_type),
            )
        finally:
            self.dll.vessel_free_metadata(c.byref(result.metadata))


@cache
def _load(path: Path) -> NativeLibrary:
    return NativeLibrary(path)


def _bundled_library() -> Path | None:
    if platform == "win32":
        name = "vessel.dll"
    elif platform == "darwin":
        name = "libvessel.dylib"
    else:
        name = "libvessel.so"
    path = Path(__file__).parent / "_lib" / name
    return path if path.is_file() else None


def load_library(path: str | PathLike[str] | None) -> NativeLibrary:
    if path is None:
        path = environ.get("VESSEL_LIBRARY_PATH") or _bundled_library()
    if not path:
        raise VesselLibraryError(
            "Install a platform wheel containing Vessel or set library_path or VESSEL_LIBRARY_PATH"
        )
    try:
        resolved = Path(path).resolve(strict=True)
    except (OSError, RuntimeError, ValueError) as error:
        raise VesselLibraryError(f"Cannot locate the Vessel shared library at {path}: {error}") from error
    return _load(resolved)
