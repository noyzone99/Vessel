package io.github.berik99.vessel;

/** Payload codecs supported by Vessel. */
public enum CompressionType {
    NONE(0), ZSTD(1), LZ4(2);

    final int code;

    CompressionType(int code) {
        this.code = code;
    }

    static CompressionType fromCode(int code) {
        return switch (code) {
            case 0 -> NONE;
            case 1 -> ZSTD;
            case 2 -> LZ4;
            default -> throw new IllegalStateException("Unknown Vessel compression type: " + code);
        };
    }
}
