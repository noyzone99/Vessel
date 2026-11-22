package io.github.noyzone99.vessel;

import java.io.IOException;
import java.io.InputStream;
import java.io.InterruptedIOException;
import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.MemorySegment;
import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.locks.ReentrantReadWriteLock;

import static java.lang.foreign.MemorySegment.NULL;
import static java.lang.foreign.ValueLayout.ADDRESS;
import static java.lang.foreign.ValueLayout.JAVA_BYTE;
import static java.lang.foreign.ValueLayout.JAVA_INT;
import static java.lang.foreign.ValueLayout.JAVA_LONG;

/** A local Vessel store. Close it with try-with-resources. */
public final class VesselStorage implements AutoCloseable {
    private static final FunctionDescriptor READ_CALLBACK = FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, JAVA_INT);
    private static final FunctionDescriptor KEY_CALLBACK = FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS);
    private static final MethodHandle SUPPLY = Native.callback(MethodHandles.lookup(), Upload.class, "supply", READ_CALLBACK);
    private static final MethodHandle COLLECT = Native.callback(MethodHandles.lookup(), Keys.class, "collect", KEY_CALLBACK);

    private final ReentrantReadWriteLock lifecycle = new ReentrantReadWriteLock();
    private MemorySegment handle;

    /** Opens or creates a store with default settings. */
    public VesselStorage(Path directory) throws IOException {
        this(directory, false);
    }

    /** Opens or creates a store and configures metadata compression. */
    public VesselStorage(Path directory, boolean enableMetadataCompression) throws IOException {
        try (Arena arena = Arena.ofConfined()) {
            MemorySegment options = arena.allocate(Native.STORE_OPTIONS);
            options.set(JAVA_INT, 0, enableMetadataCompression ? 1 : 0);
            MemorySegment output = arena.allocate(ADDRESS);
            Native.check("Open store", Native.status(Native.INIT, Native.utf8(arena, directory.toString()), options, output));
            handle = output.get(ADDRESS, 0);
        }
    }

    /** Stores bytes using the default write options. */
    public ObjectMetadata put(String key, byte[] data) throws IOException {
        return put(key, data, WriteOptions.DEFAULT);
    }

    /** Stores bytes and returns the metadata of the committed object. */
    public ObjectMetadata put(String key, byte[] data, WriteOptions options) throws IOException {
        return put(key, new Upload(Objects.requireNonNull(data, "data")), options);
    }

    /** Streams input from its current position with the default write options. */
    public ObjectMetadata put(String key, InputStream input) throws IOException {
        return put(key, input, WriteOptions.DEFAULT);
    }

    /** Streams input to EOF without taking ownership of it. */
    public ObjectMetadata put(String key, InputStream input, WriteOptions options) throws IOException {
        return put(key, new Upload(Objects.requireNonNull(input, "input")), options);
    }

    private ObjectMetadata put(String key, Upload upload, WriteOptions options) throws IOException {
        Objects.requireNonNull(options, "options");
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                MemorySegment callback = Native.upcall(SUPPLY.bindTo(upload), READ_CALLBACK, arena);
                MemorySegment result = arena.allocate(Native.METADATA);
                try {
                    int status = Native.put(handle, Native.utf8(arena, key), callback,
                        Native.objectInfo(arena, options), options, result);
                    rethrowCallback(upload.failure);
                    Native.check("Write object", status);
                    return Native.metadata(result);
                } finally {
                    Native.release(Native.FREE_METADATA, result);
                }
            }
        });
    }

    /** Reads and verifies an entire object, up to 64 MiB. */
    public byte[] read(String key) throws IOException {
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                MemorySegment output = arena.allocate(Native.OBJECT);
                try {
                    Native.check("Read object", Native.status(Native.READ, handle, Native.utf8(arena, key), output));
                    long length = output.get(JAVA_LONG, 8);
                    return length == 0 ? new byte[0] : output.get(ADDRESS, 0).reinterpret(length).toArray(JAVA_BYTE);
                } finally {
                    Native.release(Native.FREE_OBJECT, output);
                }
            }
        });
    }

    /** Opens a read-only snapshot with its own lifetime and read position. */
    public VesselInputStream open(String key) throws IOException {
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                MemorySegment output = arena.allocate(ADDRESS);
                Native.check("Open stream", Native.status(Native.STREAM_OPEN, handle, Native.utf8(arena, key), output));
                MemorySegment stream = output.get(ADDRESS, 0);
                try {
                    return new VesselInputStream(stream);
                } catch (RuntimeException | Error failure) {
                    Native.release(Native.STREAM_CLOSE, stream);
                    throw failure;
                }
            }
        });
    }

    /** Looks up metadata without opening the object payload. */
    public ObjectMetadata metadata(String key) throws IOException {
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                MemorySegment output = arena.allocate(Native.METADATA);
                try {
                    Native.check("Get metadata", Native.status(Native.GET_METADATA, handle, Native.utf8(arena, key), output));
                    return Native.metadata(output);
                } finally {
                    Native.release(Native.FREE_METADATA, output);
                }
            }
        });
    }

    /** Returns all currently visible keys. */
    public List<String> keys() throws IOException {
        return keys("");
    }

    /** Returns the visible keys starting with the given prefix. */
    public List<String> keys(String prefix) throws IOException {
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                Keys keys = new Keys();
                MemorySegment callback = Native.upcall(COLLECT.bindTo(keys), KEY_CALLBACK, arena);
                int status = Native.status(Native.GET_KEYS, handle, Native.utf8(arena, prefix), callback, NULL);
                rethrowCallback(keys.failure);
                Native.check("List keys", status);
                return List.copyOf(keys.values);
            }
        });
    }

    /** Deletes a key and returns whether it existed. */
    public boolean delete(String key) throws IOException {
        return withHandle(handle -> {
            try (Arena arena = Arena.ofConfined()) {
                MemorySegment output = arena.allocate(JAVA_INT);
                Native.check("Delete object", Native.status(Native.DELETE, handle, Native.utf8(arena, key), output));
                return output.get(JAVA_INT, 0) != 0;
            }
        });
    }

    /** Waits for active store operations and closes the store. */
    @Override
    public void close() {
        if (lifecycle.getReadHoldCount() != 0) {
            throw new IllegalStateException("Cannot close Vessel from inside a store operation");
        }
        var lock = lifecycle.writeLock();
        lock.lock();
        try {
            if (!handle.equals(NULL)) {
                Native.release(Native.DISPOSE, handle);
                handle = NULL;
            }
        } finally {
            lock.unlock();
        }
    }

    private <T> T withHandle(StoreOperation<T> operation) throws IOException {
        var lock = lifecycle.readLock();
        lock.lock();
        try {
            if (handle.equals(NULL)) {
                throw new IOException("Vessel store is closed");
            }
            return operation.run(handle);
        } finally {
            lock.unlock();
        }
    }

    @FunctionalInterface
    private interface StoreOperation<T> {
        T run(MemorySegment handle) throws IOException;
    }

    private static void rethrowCallback(Throwable failure) throws IOException {
        if (failure instanceof IOException io) {
            throw io;
        }
        if (failure instanceof RuntimeException runtime) {
            throw runtime;
        }
        if (failure instanceof Error error) {
            throw error;
        }
        if (failure != null) {
            throw new IOException(failure);
        }
    }

    private static final class Upload {
        private final InputStream input;
        private byte[] bytes;
        private int position;
        private Throwable failure;

        Upload(InputStream input) {
            this.input = input;
        }

        Upload(byte[] data) {
            this.input = null;
            this.bytes = data;
        }

        int supply(MemorySegment context, MemorySegment buffer, int capacity) {
            try {
                checkInterrupted();
                if (input == null) {
                    int count = Math.min(capacity, bytes.length - position);
                    if (count == 0) return 0;
                    MemorySegment.copy(bytes, position, buffer.reinterpret(count), JAVA_BYTE, 0, count);
                    position += count;
                    return count;
                }

                int length = Math.min(capacity, VesselInputStream.BUFFER_SIZE);
                if (bytes == null) {
                    bytes = new byte[length];
                }
                length = Math.min(length, bytes.length);
                int count = input.read(bytes, 0, length);
                checkInterrupted();
                if (count == -1) {
                    return 0;
                }
                if (count <= 0 || count > length) {
                    throw new IOException("InputStream returned an invalid byte count: " + count);
                }
                MemorySegment.copy(bytes, 0, buffer.reinterpret(count), JAVA_BYTE, 0, count);
                return count;
            } catch (Throwable error) {
                // A Java exception must never escape an FFM upcall.
                failure = error;
                return Native.CANCELLED;
            }
        }

        private static void checkInterrupted() throws InterruptedIOException {
            if (Thread.currentThread().isInterrupted()) {
                throw new InterruptedIOException("Vessel upload interrupted");
            }
        }
    }

    private static final class Keys {
        private final List<String> values = new ArrayList<>();
        private Throwable failure;

        int collect(MemorySegment context, MemorySegment key) {
            try {
                values.add(Native.string(key));
                return 0;
            } catch (Throwable error) {
                failure = error;
                return Native.CANCELLED;
            }
        }
    }
}
