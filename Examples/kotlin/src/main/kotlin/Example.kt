import io.github.noyzone99.vessel.CompressionLevel
import io.github.noyzone99.vessel.CompressionType
import io.github.noyzone99.vessel.VesselStorage
import io.github.noyzone99.vessel.WriteOptions
import java.nio.file.Path

fun main() {
    val message = "Hello from Vessel!"
    val key = "documents/hello.txt"
    val options = WriteOptions(true, CompressionType.NONE, CompressionLevel.BALANCED, null, null, null)
    VesselStorage(Path.of("my-store")).use { store ->
        message.byteInputStream().use { input -> store.put(key, input, options) }
        store.open(key).bufferedReader(Charsets.UTF_8).use { reader ->
            val stored = reader.readText()
            check(stored == message) { "Stored text differs from the original." }
            println(stored)
        }
    }
}
