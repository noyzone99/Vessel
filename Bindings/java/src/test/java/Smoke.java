import io.github.noyzone99.vessel.CompressionLevel;
import io.github.noyzone99.vessel.CompressionType;
import io.github.noyzone99.vessel.ObjectMetadata;
import io.github.noyzone99.vessel.VesselException;
import io.github.noyzone99.vessel.VesselInputStream;
import io.github.noyzone99.vessel.VesselStorage;
import io.github.noyzone99.vessel.WriteOptions;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InterruptedIOException;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;
import java.util.Objects;
import java.util.Random;

public class Smoke {
    public static void main(String[] args) throws Exception {
        Path directory = Files.createTempDirectory("vessel-java-store-");
        try {
            run(directory.resolve("caffè"));
            System.out.println("Vessel bundled Java JAR passed on " + System.getProperty("os.name")
                + " " + System.getProperty("os.arch"));
        } finally {
            try (var paths = Files.walk(directory)) {
                for (Path path : paths.sorted(Comparator.reverseOrder()).toList()) {
                    Files.delete(path);
                }
            }
        }
    }

    private static void run(Path directory) throws Exception {
        byte[] payload = new byte[200_000];
        new Random(137).nextBytes(payload);
        String hash = HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(payload));
        String key = "docs/caffè-📦.json";
        WriteOptions overwrite = new WriteOptions(true, CompressionType.NONE, CompressionLevel.BALANCED,
            null, null, null);
        ObjectMetadata last;
        VesselInputStream snapshot;
        try (var store = new VesselStorage(directory, true)) {
            for (CompressionType compression : CompressionType.values()) {
                var options = new WriteOptions(true, compression, CompressionLevel.HIGH,
                    "caffè", ".json", "application/json");
                var source = new ByteArrayInputStream(payload) {
                    @Override
                    public void close() {
                        throw new AssertionError("Vessel closed the caller's input");
                    }
                };
                ObjectMetadata metadata = store.put(key, source, options);
                equal(metadata, store.metadata(key));
                equal(key, metadata.key());
                equal(hash, metadata.contentHash());
                equal("caffè", metadata.fileName());
                equal(".json", metadata.extension());
                equal("application/json", metadata.mimeType());
                equal((long) payload.length, metadata.originalSize());
                equal(compression, metadata.compression());
                equalBytes(payload, store.read(key));
                try (var stream = store.open(key)) {
                    equal(metadata, stream.metadata());
                    equal((long) payload.length, stream.length());
                    equal(compression == CompressionType.NONE, stream.canSeek());
                    equal(0, stream.read(new byte[0]));
                    equalBytes(payload, stream.readAllBytes());
                    equal(-1, stream.read());
                    if (stream.canSeek()) {
                        stream.seek(20);
                        equal(20L, stream.position());
                        equal(Byte.toUnsignedInt(payload[20]), stream.read());
                    } else {
                        status(-7, () -> stream.seek(0));
                    }
                }
            }

            status(-4, () -> store.put(key, new byte[0]));
            status(-3, () -> store.read("absent"));
            invalid(() -> store.put(key + '\0', payload));
            invalid(() -> store.put(key + '\uD800', payload));
            equal(List.of(key), store.keys("docs/"));
            equal(List.of(), store.keys("other/"));
            equal(false, store.delete("absent"));
            store.put("empty", new byte[0]);
            equalBytes(new byte[0], store.read("empty"));
            equal(2, store.keys().size());

            IOException readFailure = new IOException("intentional input failure");
            try {
                store.put(key, new InputStream() {
                    boolean supplied;

                    @Override
                    public int read(byte[] target, int offset, int length) throws IOException {
                        if (supplied) {
                            throw readFailure;
                        }
                        supplied = true;
                        Arrays.fill(target, offset, offset + length, (byte) 7);
                        return length;
                    }

                    @Override
                    public int read() {
                        throw new AssertionError("Expected a bulk read");
                    }
                }, overwrite);
                throw new AssertionError("Expected upload to fail");
            } catch (IOException failure) {
                equal(readFailure, failure);
            }
            equalBytes(payload, store.read(key));

            Thread.currentThread().interrupt();
            try {
                store.put(key, new ByteArrayInputStream(payload), overwrite);
                throw new AssertionError("Expected upload to be interrupted");
            } catch (InterruptedIOException expected) {
                equal(true, Thread.currentThread().isInterrupted());
            } finally {
                Thread.interrupted();
            }
            equalBytes(payload, store.read(key));

            long largeSize = 64L * 1024 * 1024 + 1;
            store.put("large", new InputStream() {
                long remaining = largeSize;

                @Override
                public int read(byte[] buffer, int offset, int length) {
                    if (remaining == 0) {
                        return -1;
                    }
                    int count = (int) Math.min(remaining, length);
                    remaining -= count;
                    return count;
                }

                @Override
                public int read() {
                    throw new AssertionError("Expected a bulk read");
                }
            }, new WriteOptions(false, CompressionType.ZSTD, CompressionLevel.FASTEST, null, null, null));
            status(-9, () -> store.read("large"));
            try (var stream = store.open("large")) {
                equal(largeSize, stream.transferTo(OutputStream.nullOutputStream()));
            }

            byte[] replacement = "replacement".getBytes(StandardCharsets.UTF_8);
            snapshot = store.open(key);
            try {
                store.put(key, replacement, overwrite);
                equalBytes(replacement, store.read(key));
                equal(true, store.delete(key));
                status(-3, () -> store.metadata(key));
                last = store.put(key, replacement);
            } catch (Throwable failure) {
                snapshot.close();
                throw failure;
            }
        }
        try (snapshot) {
            equalBytes(payload, snapshot.readAllBytes());
        }
        try (var reopened = new VesselStorage(directory, true)) {
            equal(last, reopened.metadata(key));
        }
    }

    private static void equal(Object expected, Object actual) {
        if (!Objects.equals(expected, actual)) {
            throw new AssertionError("Expected " + expected + ", got " + actual);
        }
    }

    private static void equalBytes(byte[] expected, byte[] actual) {
        if (!Arrays.equals(expected, actual)) {
            throw new AssertionError("Payload differs");
        }
    }

    private static void status(int expected, IoAction action) throws IOException {
        try {
            action.run();
        } catch (VesselException failure) {
            equal(expected, failure.status());
            return;
        }
        throw new AssertionError("Expected Vessel status " + expected);
    }

    private static void invalid(IoAction action) throws IOException {
        try {
            action.run();
        } catch (IllegalArgumentException expected) {
            return;
        }
        throw new AssertionError("Expected invalid Vessel string");
    }

    @FunctionalInterface
    private interface IoAction {
        void run() throws IOException;
    }
}
