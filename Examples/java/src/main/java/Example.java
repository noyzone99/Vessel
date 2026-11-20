import io.github.noyzone99.vessel.VesselStorage;

import java.nio.charset.StandardCharsets;
import java.nio.file.Files;

public class Example {
    public static void main(String[] args) throws Exception {
        var directory = Files.createTempDirectory("vessel-example-");
        try (var store = new VesselStorage(directory)) {
            var message = "Hello from Vessel";
            store.put("notes/hello.txt", message.getBytes(StandardCharsets.UTF_8));
            var stored = new String(store.read("notes/hello.txt"), StandardCharsets.UTF_8);
            if (!message.equals(stored)) {
                throw new IllegalStateException("Stored text differs");
            }
            System.out.println(stored);
        }
    }
}
