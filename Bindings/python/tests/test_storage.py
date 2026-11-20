import hashlib
import io
import os
import tempfile
import unittest
from datetime import timezone
from pathlib import Path
from unittest.mock import Mock, patch

from vessel import (
    CompressionType,
    ObjectAlreadyExistsError,
    ObjectInfo,
    ObjectNotFoundError,
    VesselError,
    VesselLibraryError,
    VesselStorage,
)


class LibraryTests(unittest.TestCase):
    def test_missing_library_path(self):
        with (
            patch.dict(os.environ, {"VESSEL_LIBRARY_PATH": ""}),
            patch("vessel._native._bundled_library", return_value=None),
        ):
            with self.assertRaisesRegex(VesselLibraryError, "VESSEL_LIBRARY_PATH"):
                VesselStorage("unused")

    def test_abi_mismatch_precedes_binding_other_exports(self):
        dll = Mock(spec=["vessel_abi_version"])
        dll.vessel_abi_version.return_value = 99
        with patch("vessel._native.c.CDLL", return_value=dll):
            with self.assertRaisesRegex(VesselLibraryError, "ABI 99, expected 1"):
                VesselStorage("unused", library_path=__file__)


class StorageTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name) / "dépôt"
        self.store = VesselStorage(self.path)
        self.addCleanup(self.store.close)

    def test_bytes_metadata_and_keys(self):
        key = "docs/étude"
        data = b"hello\0world"
        metadata = self.store.put(key, data, info=ObjectInfo("étude.txt", ".txt", "text/plain"))
        self.assertEqual(self.store.read(key), data)
        self.assertEqual(self.store.get_metadata(key), metadata)
        self.assertEqual(metadata.file_name, "étude.txt")
        self.assertEqual(metadata.extension, ".txt")
        self.assertEqual(metadata.mime_type, "text/plain")
        self.assertEqual(metadata.original_size, len(data))
        self.assertEqual(metadata.content_hash, hashlib.sha256(data).hexdigest())
        self.assertIs(metadata.creation_date.tzinfo, timezone.utc)

        self.store.put("empty", b"")
        self.assertEqual(self.store.read("empty"), b"")
        self.assertEqual(self.store.keys("docs/"), [key])
        with self.assertRaises(ObjectAlreadyExistsError) as error:
            self.store.put(key, b"duplicate")
        self.assertEqual(error.exception.status, -4)
        self.assertTrue(self.store.delete(key))
        self.assertFalse(self.store.delete(key))
        with self.assertRaises(ObjectNotFoundError):
            self.store.get_metadata(key)

    def test_stream_upload_and_buffered_read(self):
        data = b"abc\0" * 100_000
        for codec in (CompressionType.ZSTD, CompressionType.LZ4):
            with self.subTest(codec=codec):
                key = codec.name
                with io.BytesIO(b"skip" + data) as source:
                    source.seek(4)
                    metadata = self.store.put_stream(key, source, compression=codec)
                    self.assertFalse(source.closed)
                    self.assertEqual(source.tell(), 4 + len(data))
                self.assertEqual(metadata.compression_type, codec)
                self.assertEqual(metadata.content_hash, hashlib.sha256(data).hexdigest())
                with self.store.open(key) as stream:
                    self.assertEqual(stream.length, len(data))
                    self.assertEqual(stream.metadata, metadata)
                    self.assertFalse(stream.seekable())
                    with self.assertRaises(io.UnsupportedOperation):
                        stream.seek(0)
                    with self.assertRaises(io.UnsupportedOperation):
                        stream.tell()
                    with io.BufferedReader(stream) as reader:
                        self.assertEqual(reader.read(), data)

    def test_stream_is_a_snapshot_and_releases_its_handle(self):
        self.store.put("key", b"previous")
        with self.store.open("key") as stream, self.store.open("key") as other:
            self.store.put("key", b"new", overwrite=True)
            self.assertTrue(self.store.delete("key"))
            self.store.close()
            self.store.close()
            self.assertTrue(self.store.closed)
            with self.assertRaises(ValueError):
                self.store.read("key")

            buffer = bytearray(b".....")
            self.assertEqual(stream.readinto(memoryview(buffer)[1:4]), 3)
            self.assertEqual(buffer, b".pre.")
            self.assertEqual(stream.tell(), 3)
            self.assertEqual(stream.seek(-2, os.SEEK_END), 6)
            self.assertEqual(stream.read(), b"us")
            self.assertEqual(other.read(), b"previous")
        self.assertTrue(stream.closed)
        with self.assertRaises(ValueError):
            stream.readinto(bytearray(1))
        with VesselStorage(self.path) as reopened:
            self.assertEqual(reopened.keys(), [])

    def test_upload_source_error_rolls_back_and_is_reraised(self):
        self.store.put("key", b"previous")
        failure = OSError("failed source")
        source = Mock(spec=io.RawIOBase)
        source.read.side_effect = [b"partial data", failure]
        with self.assertRaises(OSError) as error:
            self.store.put_stream("key", source, overwrite=True)
        self.assertIs(error.exception, failure)
        self.assertEqual(self.store.read("key"), b"previous")

    def test_upload_never_uses_a_reported_bytes_subclass_length(self):
        class Payload(bytes):
            def __len__(self):
                return 1

        data = Payload(b"abc")
        self.store.put("bytes", data)
        source = Mock(spec=io.RawIOBase)
        source.read.side_effect = [data, b""]
        self.store.put_stream("stream", source)
        self.assertEqual(self.store.read("bytes"), b"abc")
        self.assertEqual(self.store.read("stream"), b"abc")

    def test_large_object_can_only_be_read_as_a_stream(self):
        class Source:
            remaining = (64 << 20) + 1

            def read(self, size):
                count = min(size, self.remaining)
                self.remaining -= count
                return b"x" * count

        metadata = self.store.put_stream("large", Source(), compression=CompressionType.LZ4)
        with self.assertRaises(VesselError) as error:
            self.store.read("large")
        self.assertEqual(error.exception.status, -9)
        with self.store.open("large") as stream:
            self.assertEqual(stream.length, metadata.original_size)
            self.assertEqual(stream.read(4), b"xxxx")

    def test_rejects_nul_in_text_before_native_call(self):
        self.store.put("key", b"previous")
        with self.assertRaisesRegex(ValueError, "NUL"):
            self.store.put("key\0alias", b"changed", overwrite=True)
        with self.assertRaisesRegex(ValueError, "NUL"):
            self.store.put("key", b"changed", overwrite=True, info=ObjectInfo("file\0name"))
        self.assertEqual(self.store.read("key"), b"previous")


if __name__ == "__main__":
    unittest.main()
