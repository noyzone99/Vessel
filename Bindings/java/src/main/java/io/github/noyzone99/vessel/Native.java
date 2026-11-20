package io.github.noyzone99.vessel;

import java.io.IOException;
import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.MemoryLayout;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.SymbolLookup;
import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.time.Instant;

import static java.lang.foreign.MemorySegment.NULL;
import static java.lang.foreign.ValueLayout.ADDRESS;
import static java.lang.foreign.ValueLayout.JAVA_INT;
import static java.lang.foreign.ValueLayout.JAVA_LONG;

final class Native {
    private static final Linker LINKER = Linker.nativeLinker();
    private static final SymbolLookup SYMBOLS = NativeLibrary.load();
    private static final int ABI_VERSION = 1;
    static final int CANCELLED = -8;

    static {
        int actual = status(bind("vessel_abi_version", FunctionDescriptor.of(JAVA_INT)));
        if (actual != ABI_VERSION) {
            throw new UnsatisfiedLinkError("Vessel C ABI " + actual + " does not match required ABI " + ABI_VERSION);
        }
    }

    static final MemoryLayout STORE_OPTIONS = MemoryLayout.structLayout(
        JAVA_INT, MemoryLayout.paddingLayout(4), ADDRESS, ADDRESS);
    static final MemoryLayout OBJECT_INFO = MemoryLayout.structLayout(ADDRESS, ADDRESS, ADDRESS);
    // ABI 1, common layout on the supported 64-bit platforms.
    static final MemoryLayout METADATA = MemoryLayout.structLayout(
        JAVA_LONG, JAVA_LONG, JAVA_INT, MemoryLayout.paddingLayout(4), JAVA_LONG,
        ADDRESS, ADDRESS, ADDRESS, ADDRESS, ADDRESS, ADDRESS, JAVA_LONG);
    static final MemoryLayout OBJECT = MemoryLayout.structLayout(ADDRESS, JAVA_LONG, ADDRESS, ADDRESS, ADDRESS, ADDRESS);

    static final MethodHandle INIT = bind("vessel_init_ex", FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS));
    static final MethodHandle DISPOSE = bind("vessel_dispose", FunctionDescriptor.ofVoid(ADDRESS));
    private static final MethodHandle PUT_STREAM = bind("vessel_put_stream", FunctionDescriptor.of(JAVA_INT,
        ADDRESS, ADDRESS, ADDRESS, ADDRESS, JAVA_INT, OBJECT_INFO, JAVA_INT, JAVA_INT, ADDRESS));
    static final MethodHandle READ = bind("vessel_read", FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS));
    static final MethodHandle GET_METADATA = bind("vessel_get_metadata_ex",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS));
    static final MethodHandle GET_KEYS = bind("vessel_get_all_keys_ex",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS, ADDRESS));
    static final MethodHandle DELETE = bind("vessel_delete", FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS));
    static final MethodHandle STREAM_OPEN = bind("vessel_stream_open", FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, ADDRESS));
    private static final MethodHandle STREAM_READ = bind("vessel_stream_read",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS, JAVA_INT, ADDRESS));
    static final MethodHandle STREAM_LENGTH = bind("vessel_stream_length", FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS));
    static final MethodHandle STREAM_METADATA = bind("vessel_stream_get_metadata",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS));
    static final MethodHandle STREAM_CAN_SEEK = bind("vessel_stream_can_seek",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS));
    static final MethodHandle STREAM_POSITION = bind("vessel_stream_position",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, ADDRESS));
    static final MethodHandle STREAM_SEEK = bind("vessel_stream_seek",
        FunctionDescriptor.of(JAVA_INT, ADDRESS, JAVA_LONG, JAVA_INT));
    static final MethodHandle STREAM_CLOSE = bind("vessel_stream_close", FunctionDescriptor.ofVoid(ADDRESS));
    static final MethodHandle FREE_METADATA = bind("vessel_free_metadata", FunctionDescriptor.ofVoid(ADDRESS));
    static final MethodHandle FREE_OBJECT = bind("vessel_free_object", FunctionDescriptor.ofVoid(ADDRESS));

    private Native() {}

    private static MethodHandle bind(String name, FunctionDescriptor descriptor) {
        MemorySegment address = SYMBOLS.find(name)
            .orElseThrow(() -> new UnsatisfiedLinkError("Missing Vessel C ABI symbol: " + name));
        return LINKER.downcallHandle(address, descriptor);
    }

    static MethodHandle callback(MethodHandles.Lookup lookup, Class<?> type, String name, FunctionDescriptor descriptor) {
        try {
            return lookup.findVirtual(type, name, descriptor.toMethodType());
        } catch (ReflectiveOperationException error) {
            throw new AssertionError(error);
        }
    }

    static MemorySegment upcall(MethodHandle callback, FunctionDescriptor descriptor, Arena arena) {
        return LINKER.upcallStub(callback, descriptor, arena);
    }

    static int status(MethodHandle method, Object... args) {
        try {
            return (int) method.invokeWithArguments(args);
        } catch (Throwable failure) {
            throw linkageFailure(failure);
        }
    }

    static void release(MethodHandle method, MemorySegment value) {
        try {
            method.invokeExact(value);
        } catch (Throwable failure) {
            throw linkageFailure(failure);
        }
    }

    static int put(MemorySegment handle, MemorySegment key, MemorySegment callback, MemorySegment info,
                   WriteOptions options, MemorySegment output) {
        try {
            return (int) PUT_STREAM.invokeExact(handle, key, callback, NULL, options.overwrite() ? 1 : 0,
                info, options.compression().code, options.level().code, output);
        } catch (Throwable failure) {
            throw linkageFailure(failure);
        }
    }

    static int readStream(MemorySegment handle, MemorySegment buffer, int length, MemorySegment output) throws IOException {
        try {
            check("Read stream", (int) STREAM_READ.invokeExact(handle, buffer, length, output));
        } catch (VesselException failure) {
            throw failure;
        } catch (Throwable failure) {
            throw linkageFailure(failure);
        }
        return output.get(JAVA_INT, 0);
    }

    private static RuntimeException linkageFailure(Throwable failure) {
        if (failure instanceof RuntimeException runtime) {
            return runtime;
        }
        if (failure instanceof Error error) {
            throw error;
        }
        throw new AssertionError(failure);
    }

    static void check(String operation, int status) throws VesselException {
        if (status != 0) {
            throw new VesselException(operation, status);
        }
    }

    static MemorySegment utf8(Arena arena, String value) {
        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);
            if (character == '\0') {
                throw new IllegalArgumentException("Vessel strings cannot contain NUL characters");
            }
            if (Character.isSurrogate(character)) {
                if (!Character.isHighSurrogate(character) || index + 1 == value.length()
                        || !Character.isLowSurrogate(value.charAt(index + 1))) {
                    throw new IllegalArgumentException("Vessel strings must be well-formed Unicode");
                }
                index++;
            }
        }
        return arena.allocateFrom(value);
    }

    static MemorySegment objectInfo(Arena arena, WriteOptions options) {
        MemorySegment info = arena.allocate(OBJECT_INFO);
        info.setAtIndex(ADDRESS, 0, options.fileName() == null ? NULL : utf8(arena, options.fileName()));
        info.setAtIndex(ADDRESS, 1, options.extension() == null ? NULL : utf8(arena, options.extension()));
        info.setAtIndex(ADDRESS, 2, options.mimeType() == null ? NULL : utf8(arena, options.mimeType()));
        return info;
    }

    static String string(MemorySegment pointer) {
        return pointer.equals(NULL) ? null : pointer.reinterpret(Integer.MAX_VALUE).getString(0);
    }

    static ObjectMetadata metadata(MemorySegment result) {
        return new ObjectMetadata(string(result.get(ADDRESS, 32)), string(result.get(ADDRESS, 40)),
            string(result.get(ADDRESS, 48)), result.get(JAVA_LONG, 0), result.get(JAVA_LONG, 8),
            CompressionType.fromCode(result.get(JAVA_INT, 16)), Instant.ofEpochMilli(result.get(JAVA_LONG, 80)),
            string(result.get(ADDRESS, 56)), string(result.get(ADDRESS, 64)), string(result.get(ADDRESS, 72)));
    }
}
