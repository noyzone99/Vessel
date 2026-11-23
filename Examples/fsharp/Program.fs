open System
open System.IO
open System.Text
open Vessel

let run () = task {
    let message = "Hello from Vessel!"
    use storage = new VesselStorage("my-store")
    use input = new MemoryStream(Encoding.UTF8.GetBytes(message))
    let! metadata = storage.PutAsync("documents/hello.txt", input, overwrite = true)

    use content = storage.GetStream(metadata.Key)
    use reader = new StreamReader(content, Encoding.UTF8, leaveOpen = true)
    let! stored = reader.ReadToEndAsync()
    if stored <> message then
        raise (InvalidDataException("Stored text differs from the original."))

    Console.WriteLine(stored)
}

[<EntryPoint>]
let main _ =
    run().GetAwaiter().GetResult()
    0
