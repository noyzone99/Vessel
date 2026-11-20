"""Python bindings for the Vessel embedded object store."""

from .storage import VesselStorage, VesselStream
from .types import (
    CompressionLevel,
    CompressionType,
    ObjectAlreadyExistsError,
    ObjectInfo,
    ObjectMetadata,
    ObjectNotFoundError,
    VesselError,
    VesselLibraryError,
)

__all__ = [
    "CompressionLevel",
    "CompressionType",
    "ObjectAlreadyExistsError",
    "ObjectInfo",
    "ObjectMetadata",
    "ObjectNotFoundError",
    "VesselError",
    "VesselLibraryError",
    "VesselStorage",
    "VesselStream",
]
