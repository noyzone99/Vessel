package io.github.berik99.vessel;

/** Presets for the selected payload codec. */
public enum CompressionLevel {
    FASTEST(0), BALANCED(1), HIGH(2), ULTRA(3);

    final int code;

    CompressionLevel(int code) {
        this.code = code;
    }
}
