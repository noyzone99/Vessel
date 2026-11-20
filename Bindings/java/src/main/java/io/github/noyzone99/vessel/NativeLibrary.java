package io.github.noyzone99.vessel;

import java.io.IOException;
import java.io.InputStream;
import java.lang.foreign.Arena;
import java.lang.foreign.SymbolLookup;
import java.nio.charset.StandardCharsets;
import java.nio.file.FileAlreadyExistsException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.security.DigestInputStream;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.Locale;

final class NativeLibrary {
    private NativeLibrary() {}

    static SymbolLookup load() {
        String override = System.getProperty("vessel.library.path");
        try {
            Path library = override == null ? extract() : Path.of(override).toAbsolutePath();
            try {
                // NativeAOT does not support unloading its shared library.
                return SymbolLookup.libraryLookup(library, Arena.global());
            } catch (IllegalArgumentException error) {
                throw new IOException("Cannot load " + library, error);
            }
        } catch (IOException error) {
            UnsatisfiedLinkError failure = new UnsatisfiedLinkError(
                error.getMessage() + ". Set -Dvessel.library.path to a compatible Vessel shared library.");
            failure.initCause(error);
            throw failure;
        }
    }

    private static Path extract() throws IOException {
        String os = System.getProperty("os.name").toLowerCase(Locale.ROOT);
        String architecture = switch (System.getProperty("os.arch")) {
            case "amd64", "x86_64" -> "x64";
            case "aarch64", "arm64" -> "arm64";
            default -> throw new IOException("Unsupported JVM architecture: " + System.getProperty("os.arch"));
        };
        boolean windows = os.startsWith("windows");
        String target;
        String file;
        if (windows) {
            target = "win";
            file = "vessel.dll";
        } else if (os.startsWith("mac")) {
            target = "osx";
            file = "libvessel.dylib";
        } else if (os.equals("linux")) {
            target = linuxTarget();
            file = "libvessel.so";
        } else {
            throw new IOException("Unsupported OS: " + os);
        }

        String resource = "/META-INF/vessel/native/" + target + "-" + architecture + "/" + file;
        try (InputStream input = NativeLibrary.class.getResourceAsStream(resource)) {
            if (input == null) {
                throw new IOException("Missing native library resource " + resource);
            }
            if (windows) {
                return extractWindows(input, file);
            }
            Path directory = Files.createTempDirectory("vessel-");
            Path output = directory.resolve(file);
            directory.toFile().deleteOnExit();
            output.toFile().deleteOnExit();
            Files.copy(input, output);
            return output.toAbsolutePath();
        }
    }

    private static String linuxTarget() throws IOException {
        // Inspect the JVM process so a compatibility package cannot select the wrong libc.
        try (var maps = Files.newBufferedReader(Path.of("/proc/self/maps"), StandardCharsets.ISO_8859_1)) {
            boolean glibc = false;
            for (String line; (line = maps.readLine()) != null;) {
                if (line.contains("/ld-musl-") || line.contains("/libc.musl-")) {
                    return "linux-musl";
                }
                glibc |= line.contains("/ld-linux-") || line.endsWith("/libc.so.6");
            }
            if (glibc) {
                return "linux";
            }
        }
        throw new IOException("Cannot determine the C library used by the JVM");
    }

    private static Path extractWindows(InputStream input, String file) throws IOException {
        // Windows locks loaded DLLs. Reuse an immutable file keyed by its content.
        Path cache = Path.of(System.getProperty("user.home"), ".vessel", "native").toAbsolutePath();
        Files.createDirectories(cache);
        Path staging = Files.createTempFile(cache, "vessel-", ".tmp");
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            Files.copy(new DigestInputStream(input, digest), staging, StandardCopyOption.REPLACE_EXISTING);
            Path output = cache.resolve(HexFormat.of().formatHex(digest.digest()) + "-" + file);
            try {
                Files.move(staging, output);
            } catch (FileAlreadyExistsException existing) {
                if (Files.mismatch(staging, output) != -1) {
                    throw new IOException("Cached Vessel library differs from the JAR: " + output, existing);
                }
            }
            return output;
        } catch (NoSuchAlgorithmException impossible) {
            throw new AssertionError(impossible);
        } finally {
            Files.deleteIfExists(staging);
        }
    }
}
