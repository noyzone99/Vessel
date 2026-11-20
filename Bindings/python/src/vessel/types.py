from dataclasses import dataclass
from datetime import datetime
from enum import IntEnum


class CompressionType(IntEnum):
    NONE = 0
    ZSTD = 1
    LZ4 = 2


class CompressionLevel(IntEnum):
    FASTEST = 0
    BALANCED = 1
    HIGH = 2
    ULTRA = 3


@dataclass(frozen=True, slots=True)
class ObjectInfo:
    file_name: str | None = None
    extension: str | None = None
    mime_type: str | None = None


@dataclass(frozen=True, slots=True)
class ObjectMetadata:
    key: str
    hash: str
    etag: str
    original_size: int
    compressed_size: int
    compression_type: CompressionType
    creation_date: datetime
    file_name: str | None
    extension: str | None
    mime_type: str | None

    @property
    def content_hash(self) -> str:
        """The SHA-256 digest of the uncompressed content."""
        return self.hash[:64] if len(self.hash) == 66 else self.hash


class VesselError(RuntimeError):
    """A failed call to the native library."""

    def __init__(self, status: int, message: str) -> None:
        self.status = status
        super().__init__(status, message)

    def __str__(self) -> str:
        return f"{self.args[1]} (Vessel status {self.status})"


class ObjectNotFoundError(VesselError):
    """The requested object does not exist."""


class ObjectAlreadyExistsError(VesselError):
    """The key already exists and overwrite was disabled."""


class VesselLibraryError(RuntimeError):
    """The shared library could not be located, loaded or bound."""
