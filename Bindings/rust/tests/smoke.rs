use std::io::{self, Read, Seek, SeekFrom};
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use vessel::{Compression, Error, ObjectInfo, OpenOptions, Vessel, WriteOptions};

struct TestDirectory(PathBuf);

impl TestDirectory {
    fn new() -> Self {
        static NEXT: AtomicUsize = AtomicUsize::new(0);
        loop {
            let id = NEXT.fetch_add(1, Ordering::Relaxed);
            let path =
                std::env::temp_dir().join(format!("vessel-rust-{}-{id}", std::process::id()));
            match std::fs::create_dir(&path) {
                Ok(()) => return Self(path),
                Err(error) if error.kind() == io::ErrorKind::AlreadyExists => continue,
                Err(error) => panic!("cannot create {}: {error}", path.display()),
            }
        }
    }
}

impl Drop for TestDirectory {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

#[test]
fn reads_the_opened_version_after_replacement_deletion_and_store_drop() {
    let directory = TestDirectory::new();
    let store = Vessel::open_with_options(
        &directory.0,
        OpenOptions {
            enable_metadata_compression: true,
        },
    )
    .unwrap();
    let key = "documents/caffè.bin";
    let original = b"a\0b";
    let options = WriteOptions {
        info: ObjectInfo {
            file_name: Some("caffè.bin".into()),
            extension: Some(".bin".into()),
            mime_type: Some("application/octet-stream".into()),
        },
        ..WriteOptions::default()
    };

    let metadata = store.put(key, original.as_slice(), &options).unwrap();
    assert_eq!(metadata, store.metadata(key).unwrap());
    assert_eq!(metadata.original_size, 3);
    assert_eq!(metadata.info.file_name.as_deref(), Some("caffè.bin"));
    assert_eq!(metadata.content_hash().len(), 64);
    assert_eq!(store.keys("documents/").unwrap(), vec![key.to_owned()]);
    assert!(matches!(
        store.put(key, b"duplicate".as_slice(), &options),
        Err(Error::Native(-4))
    ));

    let mut snapshot = store.open_read(key).unwrap();
    assert_eq!(snapshot.metadata().unwrap(), metadata);
    assert_eq!(snapshot.size().unwrap(), 3);
    assert!(snapshot.can_seek().unwrap());
    assert_eq!(snapshot.seek(SeekFrom::End(-1)).unwrap(), 2);
    let mut last = [0];
    snapshot.read_exact(&mut last).unwrap();
    assert_eq!(&last, b"b");
    snapshot.rewind().unwrap();

    let replacement = vec![b'c'; 256 * 1024];
    let options = WriteOptions {
        overwrite: true,
        compression: Compression::Zstd,
        ..options
    };
    store.put(key, replacement.as_slice(), &options).unwrap();
    let mut compressed = store.open_read(key).unwrap();
    assert!(!compressed.can_seek().unwrap());
    assert_eq!(compressed.size().unwrap(), replacement.len() as u64);
    assert_eq!(
        compressed.metadata().unwrap().compression,
        Compression::Zstd
    );
    assert_eq!(
        compressed.rewind().unwrap_err().kind(),
        io::ErrorKind::Unsupported
    );
    let mut actual = Vec::new();
    compressed.read_to_end(&mut actual).unwrap();
    assert_eq!(actual, replacement);

    assert!(store.delete(key).unwrap());
    assert!(!store.delete(key).unwrap());
    assert!(matches!(store.open_read(key), Err(Error::Native(-3))));
    drop(compressed);
    drop(store);

    let mut actual = Vec::new();
    snapshot.read_to_end(&mut actual).unwrap();
    assert_eq!(&actual, original);
}

struct BrokenInput {
    first_chunk: bool,
    panic: bool,
}

impl Read for BrokenInput {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        if self.first_chunk {
            self.first_chunk = false;
            buffer.fill(b'x');
            return Ok(buffer.len());
        }
        assert!(!self.panic, "test input panic");
        Err(io::Error::other("test input failure"))
    }
}

#[test]
fn preserves_the_previous_version_after_input_errors_and_panics() {
    let directory = TestDirectory::new();
    let store = Vessel::open(&directory.0).unwrap();
    let key = "documents/original.bin";
    let options = WriteOptions {
        overwrite: true,
        ..WriteOptions::default()
    };
    let original = b"original";
    store.put(key, original.as_slice(), &options).unwrap();

    let error = store
        .put(
            key,
            BrokenInput {
                first_chunk: true,
                panic: false,
            },
            &options,
        )
        .unwrap_err();
    assert!(matches!(error, Error::Input(error) if error.to_string() == "test input failure"));
    let result = catch_unwind(AssertUnwindSafe(|| {
        store.put(
            key,
            BrokenInput {
                first_chunk: true,
                panic: true,
            },
            &options,
        )
    }));
    assert!(result.is_err());

    let mut actual = Vec::new();
    store
        .open_read(key)
        .unwrap()
        .read_to_end(&mut actual)
        .unwrap();
    assert_eq!(&actual, original);
    assert!(matches!(
        store.put("invalid\0key", original.as_slice(), &options),
        Err(Error::InvalidText(_))
    ));
}
