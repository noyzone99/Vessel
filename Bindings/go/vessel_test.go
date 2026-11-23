package vessel

import (
	"bytes"
	"errors"
	"io"
	"slices"
	"strings"
	"testing"
	"testing/iotest"
)

func TestStreamingOperations(t *testing.T) {
	store, err := Open(t.TempDir(), nil)
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	const key = "documents/日本語.bin"
	original := bytes.Repeat([]byte{0, 3, 5, 255}, 32*1024)
	metadata, err := store.Put(key, iotest.DataErrReader(bytes.NewReader(original)), &WriteOptions{
		Compression: Zstd, FileName: "日本語.bin", MIMEType: "application/octet-stream",
	})
	if err != nil || metadata.OriginalSize != int64(len(original)) || metadata.Compression != Zstd || metadata.FileName != "日本語.bin" {
		t.Fatalf("write metadata = %+v, %v", metadata, err)
	}
	listed, err := store.Keys("documents/")
	if err != nil || !slices.Equal(listed, []string{key}) {
		t.Fatalf("keys = %v, %v", listed, err)
	}
	if _, err := store.Put(key, strings.NewReader("duplicate"), nil); !errors.Is(err, ErrAlreadyExists) {
		t.Fatalf("duplicate error = %v", err)
	}

	failed := errors.New("test source error")
	options := &WriteOptions{Overwrite: true}
	if _, err := store.Put(key, io.MultiReader(strings.NewReader("partial"), iotest.ErrReader(failed)), options); !errors.Is(err, failed) {
		t.Fatalf("read error = %v", err)
	}
	stored, err := store.Metadata(key)
	if err != nil || stored.ETag != metadata.ETag {
		t.Fatalf("metadata after cancelled upload = %+v, %v", stored, err)
	}

	stream, err := store.Open(key)
	if err != nil {
		t.Fatal(err)
	}
	defer stream.Close()
	if _, err := stream.Seek(0, io.SeekStart); !errors.Is(err, ErrInvalidOperation) {
		t.Fatalf("compressed seek error = %v", err)
	}
	if _, err := store.Put(key, strings.NewReader("replacement"), options); err != nil {
		t.Fatal(err)
	}
	if deleted, err := store.Delete(key); err != nil || !deleted {
		t.Fatalf("delete = %v, %v", deleted, err)
	}
	if _, err := store.Metadata(key); !errors.Is(err, ErrNotFound) {
		t.Fatalf("metadata after delete error = %v", err)
	}
	if err := store.Close(); err != nil {
		t.Fatal(err)
	}
	if _, err := store.Keys(""); !errors.Is(err, ErrDisposed) {
		t.Fatalf("keys on closed store error = %v", err)
	}
	contents, err := io.ReadAll(stream)
	if err != nil || !bytes.Equal(contents, original) {
		t.Fatalf("read snapshot = %d bytes, %v", len(contents), err)
	}
}
