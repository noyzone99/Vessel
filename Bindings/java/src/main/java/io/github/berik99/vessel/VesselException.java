package io.github.berik99.vessel;

import java.io.IOException;

/** A failed operation reported by the Vessel C ABI. */
public final class VesselException extends IOException {
    private static final long serialVersionUID = 1L;
    private final int status;

    VesselException(String operation, int status) {
        super(operation + ": " + switch (status) {
            case -2 -> "invalid argument";
            case -3 -> "object not found";
            case -4 -> "object already exists";
            case -5 -> "I/O failure";
            case -6 -> "handle disposed";
            case -7 -> "invalid operation";
            case -8 -> "operation cancelled";
            case -9 -> "limit exceeded";
            case -10 -> "out of memory";
            default -> "native failure";
        } + " (Vessel status " + status + ")");
        this.status = status;
    }

    /** Returns the status code defined by vessel.h. */
    public int status() {
        return status;
    }
}
