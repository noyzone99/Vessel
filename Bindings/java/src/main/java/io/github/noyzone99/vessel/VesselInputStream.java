package io.github.noyzone99.vessel;

import java.io.IOException;
import java.io.InputStream;
import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.invoke.MethodHandle;
import java.util.Objects;

import static java.lang.foreign.MemorySegment.NULL;
import static java.lang.foreign.ValueLayout.JAVA_BYTE;
import static java.lang.foreign.ValueLayout.JAVA_INT;
import static java.lang.foreign.ValueLayout.JAVA_LONG;

/** An independent read-only snapshot of a Vessel object. */
public final class VesselInputStream extends InputStream {
    static final int BUFFER_SIZE = 64 * 1024;
    private final Arena arena = Arena.ofShared();
    private MemorySegment handle;
    private MemorySegment buffer;
    private MemorySegment bytesRead;

    VesselInputStream(MemorySegment handle) {
        this.handle = handle;
    }

    @Override
    public synchronized int read() throws IOException {
        return readChunk(1) == 0 ? -1 : Byte.toUnsignedInt(buffer.get(JAVA_BYTE, 0));
    }

    @Override
    public synchronized int read(byte[] destination, int offset, int length) throws IOException {
        Objects.checkFromIndexSize(offset, length, destination.length);
        if (length == 0) {
            requireOpen();
            return 0;
        }
        int read = readChunk(Math.min(length, BUFFER_SIZE));
        if (read == 0) {
            return -1;
        }
        MemorySegment.copy(buffer, JAVA_BYTE, 0, destination, offset, read);
        return read;
    }

    private int readChunk(int length) throws IOException {
        requireOpen();
        if (buffer == null) {
            bytesRead = arena.allocate(JAVA_INT);
            buffer = arena.allocate(BUFFER_SIZE);
        }
        return Native.readStream(handle, buffer, length, bytesRead);
    }

    /** Returns the uncompressed length in bytes. */
    public synchronized long length() throws IOException {
        return readLong(Native.STREAM_LENGTH, "Get stream length");
    }

    /** Returns the metadata captured when the stream opened. */
    public synchronized ObjectMetadata metadata() throws IOException {
        requireOpen();
        try (Arena scratch = Arena.ofConfined()) {
            MemorySegment output = scratch.allocate(Native.METADATA);
            try {
                Native.check("Get stream metadata", Native.status(Native.STREAM_METADATA, handle, output));
                return Native.metadata(output);
            } finally {
                Native.release(Native.FREE_METADATA, output);
            }
        }
    }

    /** Returns whether the stream supports reading or setting its position. */
    public synchronized boolean canSeek() throws IOException {
        requireOpen();
        try (Arena scratch = Arena.ofConfined()) {
            MemorySegment output = scratch.allocate(JAVA_INT);
            Native.check("Check stream seek", Native.status(Native.STREAM_CAN_SEEK, handle, output));
            return output.get(JAVA_INT, 0) != 0;
        }
    }

    /** Returns the current byte position of an uncompressed stream. */
    public synchronized long position() throws IOException {
        return readLong(Native.STREAM_POSITION, "Get stream position");
    }

    /** Sets the absolute byte position of an uncompressed stream. */
    public synchronized void seek(long position) throws IOException {
        requireOpen();
        Native.check("Seek stream", Native.status(Native.STREAM_SEEK, handle, position, 0));
    }

    private long readLong(MethodHandle method, String operation) throws IOException {
        requireOpen();
        try (Arena scratch = Arena.ofConfined()) {
            MemorySegment output = scratch.allocate(JAVA_LONG);
            Native.check(operation, Native.status(method, handle, output));
            return output.get(JAVA_LONG, 0);
        }
    }

    private void requireOpen() throws IOException {
        if (handle.equals(NULL)) {
            throw new IOException("Vessel stream is closed");
        }
    }

    /** Releases the native stream and its read buffer. */
    @Override
    public synchronized void close() {
        if (!handle.equals(NULL)) {
            try {
                Native.release(Native.STREAM_CLOSE, handle);
            } finally {
                handle = NULL;
                arena.close();
            }
        }
    }
}
