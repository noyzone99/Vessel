package vessel

import (
	"io"
	"strings"
	"testing"
)

func TestNilPanicCancelsUpload(t *testing.T) {
	t.Setenv("GODEBUG", "panicnil=1")
	store, err := Open(t.TempDir(), nil)
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	if _, err := store.Put("key", strings.NewReader("original"), nil); err != nil {
		t.Fatal(err)
	}

	func() {
		defer func() { _ = recover() }()
		_, _ = store.Put("key", &nilPanicReader{}, &WriteOptions{Overwrite: true})
		t.Error("the upload returned after the input panicked")
	}()

	stream, err := store.Open("key")
	if err != nil {
		t.Fatal(err)
	}
	defer stream.Close()
	payload, err := io.ReadAll(stream)
	if err != nil || string(payload) != "original" {
		t.Fatalf("read after cancelled overwrite = %q, %v", payload, err)
	}
}

type nilPanicReader struct{ read bool }

func (r *nilPanicReader) Read(buffer []byte) (int, error) {
	if r.read {
		panic(nil)
	}
	r.read = true
	return copy(buffer, "partial"), nil
}
