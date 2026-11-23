use std::io::{self, Write};
use vessel::{Vessel, WriteOptions};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let store = Vessel::open("my-store")?;
    let key = "documents/hello.txt";
    let payload = b"Hello from Vessel!";
    let options = WriteOptions {
        overwrite: true,
        ..WriteOptions::default()
    };
    store.put(key, payload.as_slice(), &options)?;

    let mut reader = store.open_read(key)?;
    let mut output = io::stdout().lock();
    io::copy(&mut reader, &mut output)?;
    writeln!(output)?;
    Ok(())
}
