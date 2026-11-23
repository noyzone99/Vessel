import io.github.noyzone99.vessel.CompressionLevel;
import io.github.noyzone99.vessel.CompressionType;
import io.github.noyzone99.vessel.VesselStorage;
import io.github.noyzone99.vessel.WriteOptions;

import java.io.ByteArrayInputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;

public class Example {
    public static void main(String[] args) throws Exception {
        var message = "Hello from Vessel!";
        var key = "documents/hello.txt";
        var options = new WriteOptions(true, CompressionType.NONE, CompressionLevel.BALANCED,
            null, null, null);
        try (var store = new VesselStorage(Path.of("my-store"));
             var input = new ByteArrayInputStream(message.getBytes(StandardCharsets.UTF_8))) {
            store.put(key, input, options);
            try (var content = store.open(key)) {
                var stored = new String(content.readAllBytes(), StandardCharsets.UTF_8);
                if (!message.equals(stored)) {
                    throw new IllegalStateException("Stored text differs from the original.");
                }
                System.out.println(stored);
            }
        }
    }
}
