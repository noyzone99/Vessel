package io.github.noyzone99.vessel;

import java.util.Objects;

/** Compression, conflict handling and optional file metadata for a write. */
public record WriteOptions(boolean overwrite, CompressionType compression, CompressionLevel level,
                           String fileName, String extension, String mimeType) {
    /** Keeps existing objects, disables compression and omits file metadata. */
    public static final WriteOptions DEFAULT =
        new WriteOptions(false, CompressionType.NONE, CompressionLevel.BALANCED, null, null, null);

    public WriteOptions {
        Objects.requireNonNull(compression, "compression");
        Objects.requireNonNull(level, "level");
    }
}
