package io.github.berik99.vessel;

import java.time.Instant;

/** Metadata for the version of an object observed by an operation. */
public record ObjectMetadata(String key, String hash, String etag, long originalSize, long compressedSize,
                             CompressionType compression, Instant creationDate,
                             String fileName, String extension, String mimeType) {
    /** Returns the SHA-256 digest of the uncompressed payload. */
    public String contentHash() {
        return hash.length() == 66 ? hash.substring(0, 64) : hash;
    }
}
