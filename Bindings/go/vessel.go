// Package vessel provides streaming access to the Vessel embedded object store.
package vessel

/*
#cgo CPPFLAGS: -I${SRCDIR}/../../Sources/Vessel.Native
#cgo LDFLAGS: -lvessel
#include <stdlib.h>
#include "bridge.h"
*/
import "C"

import (
	"fmt"
	"io"
	"math"
	"runtime/cgo"
	"strings"
	"sync"
	"time"
	"unicode/utf8"
	"unsafe"
)

// Vessel is an open store. Operations can run concurrently. A Vessel must not
// be copied after first use.
type Vessel struct {
	mu     sync.RWMutex
	handle C.uintptr_t
}

// Open opens or creates a store. A nil options pointer selects the defaults.
func Open(directory string, options *Options) (*Vessel, error) {
	if version := C.vessel_abi_version(); version != C.VESSEL_ABI_VERSION {
		return nil, fmt.Errorf("vessel: C ABI %d required, found %d", C.VESSEL_ABI_VERSION, version)
	}
	path, err := cText(directory, "store directory")
	if err != nil {
		return nil, err
	}
	defer C.free(unsafe.Pointer(path))

	var compress C.int32_t
	if options != nil && options.EnableMetadataCompression {
		compress = 1
	}
	var handle C.uintptr_t
	if err := nativeError(C.go_vessel_open(path, compress, &handle)); err != nil {
		return nil, err
	}
	return &Vessel{handle: handle}, nil
}

// Close waits for active operations and releases the store. Streams opened
// before Close remain readable. Close may be called more than once.
func (v *Vessel) Close() error {
	v.mu.Lock()
	defer v.mu.Unlock()
	if v.handle != 0 {
		C.go_vessel_close(v.handle)
		v.handle = 0
	}
	return nil
}

// Put consumes input to EOF and returns the committed metadata. The caller owns
// input. A read error cancels the write and is returned to the caller. The reader
// must not call methods on the same store while its upload is in progress.
func (v *Vessel) Put(key string, input io.Reader, options *WriteOptions) (ObjectMetadata, error) {
	if input == nil {
		return ObjectMetadata{}, fmt.Errorf("%w: nil upload reader", ErrInvalidArgument)
	}
	cKey, err := cText(key, "key")
	if err != nil {
		return ObjectMetadata{}, err
	}
	defer C.free(unsafe.Pointer(cKey))

	var write WriteOptions
	if options != nil {
		write = *options
	}
	if write.Compression < None || write.Compression > LZ4 || write.CompressionLevel < Balanced || write.CompressionLevel > Ultra {
		return ObjectMetadata{}, fmt.Errorf("%w: invalid compression type or level", ErrInvalidArgument)
	}
	level := C.int32_t(write.CompressionLevel)
	switch write.CompressionLevel {
	case Balanced:
		level = C.VESSEL_LEVEL_BALANCED
	case Fastest:
		level = C.VESSEL_LEVEL_FASTEST
	}
	var overwrite C.int32_t
	if write.Overwrite {
		overwrite = 1
	}
	var info C.vessel_object_info_t
	for _, field := range []struct {
		name  string
		value string
		out   **C.char
	}{
		{"file name", write.FileName, &info.file_name},
		{"extension", write.Extension, &info.extension},
		{"MIME type", write.MIMEType, &info.mime_type},
	} {
		if field.value == "" {
			continue
		}
		value, err := cText(field.value, field.name)
		if err != nil {
			return ObjectMetadata{}, err
		}
		defer C.free(unsafe.Pointer(value))
		*field.out = value
	}

	v.mu.RLock()
	defer v.mu.RUnlock()
	if v.handle == 0 {
		return ObjectMetadata{}, ErrDisposed
	}
	state := uploadReader{reader: input}
	reader := cgo.NewHandle(&state)
	defer reader.Delete()
	var result C.vessel_metadata_result_t
	err = nativeError(C.go_vessel_put(v.handle, cKey, C.uintptr_t(reader), overwrite, info,
		C.int32_t(write.Compression), level, &result))
	defer C.vessel_free_metadata(&result.metadata)
	if state.panicked {
		panic(state.panicValue)
	}
	if state.err != nil {
		return ObjectMetadata{}, state.err
	}
	if err != nil {
		return ObjectMetadata{}, err
	}
	return metadata(result), nil
}

// Open returns a stream over the committed version selected at open time.
// The caller owns the stream and must close it.
func (v *Vessel) Open(key string) (*Stream, error) {
	cKey, err := cText(key, "key")
	if err != nil {
		return nil, err
	}
	defer C.free(unsafe.Pointer(cKey))
	v.mu.RLock()
	defer v.mu.RUnlock()
	if v.handle == 0 {
		return nil, ErrDisposed
	}
	var stream C.uintptr_t
	if err := nativeError(C.go_vessel_stream_open(v.handle, cKey, &stream)); err != nil {
		return nil, err
	}
	return &Stream{handle: stream}, nil
}

// Metadata returns the committed object's metadata without reading its payload.
func (v *Vessel) Metadata(key string) (ObjectMetadata, error) {
	cKey, err := cText(key, "key")
	if err != nil {
		return ObjectMetadata{}, err
	}
	defer C.free(unsafe.Pointer(cKey))
	v.mu.RLock()
	defer v.mu.RUnlock()
	if v.handle == 0 {
		return ObjectMetadata{}, ErrDisposed
	}
	var result C.vessel_metadata_result_t
	if err := nativeError(C.go_vessel_metadata(v.handle, cKey, &result)); err != nil {
		return ObjectMetadata{}, err
	}
	defer C.vessel_free_metadata(&result.metadata)
	return metadata(result), nil
}

// Keys returns visible keys with the given prefix. An empty prefix includes all keys.
func (v *Vessel) Keys(prefix string) ([]string, error) {
	cPrefix, err := cText(prefix, "prefix")
	if err != nil {
		return nil, err
	}
	defer C.free(unsafe.Pointer(cPrefix))
	v.mu.RLock()
	defer v.mu.RUnlock()
	if v.handle == 0 {
		return nil, ErrDisposed
	}
	var keys []string
	handle := cgo.NewHandle(&keys)
	defer handle.Delete()
	if err := nativeError(C.go_vessel_keys(v.handle, cPrefix, C.uintptr_t(handle))); err != nil {
		return nil, err
	}
	return keys, nil
}

// Delete removes a key and reports whether it existed.
func (v *Vessel) Delete(key string) (bool, error) {
	cKey, err := cText(key, "key")
	if err != nil {
		return false, err
	}
	defer C.free(unsafe.Pointer(cKey))
	v.mu.RLock()
	defer v.mu.RUnlock()
	if v.handle == 0 {
		return false, ErrDisposed
	}
	var deleted C.int32_t
	err = nativeError(C.go_vessel_delete(v.handle, cKey, &deleted))
	return deleted != 0, err
}

// Stream is a read-only object snapshot that implements io.ReadSeekCloser.
// A Stream must not be copied after first use.
type Stream struct {
	mu     sync.Mutex
	handle C.uintptr_t
}

// Read reads the next bytes into the caller's buffer and reports io.EOF at the end.
func (s *Stream) Read(data []byte) (int, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle == 0 {
		return 0, ErrDisposed
	}
	if len(data) == 0 {
		return 0, nil
	}
	var count C.int32_t
	if err := nativeError(C.go_vessel_stream_read(s.handle, (*C.uint8_t)(unsafe.Pointer(&data[0])),
		C.int32_t(min(len(data), math.MaxInt32)), &count)); err != nil {
		return 0, err
	}
	if count == 0 {
		return 0, io.EOF
	}
	return int(count), nil
}

// Size returns the uncompressed size in bytes.
func (s *Stream) Size() (int64, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle == 0 {
		return 0, ErrDisposed
	}
	var size C.int64_t
	err := nativeError(C.go_vessel_stream_size(s.handle, &size))
	return int64(size), err
}

// Metadata returns the metadata for the stream's snapshot.
func (s *Stream) Metadata() (ObjectMetadata, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle == 0 {
		return ObjectMetadata{}, ErrDisposed
	}
	var result C.vessel_metadata_result_t
	if err := nativeError(C.go_vessel_stream_metadata(s.handle, &result)); err != nil {
		return ObjectMetadata{}, err
	}
	defer C.vessel_free_metadata(&result.metadata)
	return metadata(result), nil
}

// CanSeek reports whether Seek is supported for this object.
func (s *Stream) CanSeek() (bool, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle == 0 {
		return false, ErrDisposed
	}
	var canSeek C.int32_t
	err := nativeError(C.go_vessel_stream_can_seek(s.handle, &canSeek))
	return canSeek != 0, err
}

// Seek changes the position of an uncompressed stream. Compressed streams
// return ErrInvalidOperation.
func (s *Stream) Seek(offset int64, whence int) (int64, error) {
	if whence < io.SeekStart || whence > io.SeekEnd {
		return 0, ErrInvalidArgument
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle == 0 {
		return 0, ErrDisposed
	}
	var position C.int64_t
	err := nativeError(C.go_vessel_stream_seek(s.handle, C.int64_t(offset), C.int32_t(whence), &position))
	return int64(position), err
}

// Close releases the stream. Close may be called more than once.
func (s *Stream) Close() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.handle != 0 {
		C.go_vessel_stream_close(s.handle)
		s.handle = 0
	}
	return nil
}

func cText(value, field string) (*C.char, error) {
	if !utf8.ValidString(value) || strings.ContainsRune(value, 0) {
		return nil, fmt.Errorf("%w: %s must be valid UTF-8 without NUL", ErrInvalidArgument, field)
	}
	return C.CString(value), nil
}

func nativeError(status C.vessel_error_t) error {
	if status == C.VESSEL_SUCCESS {
		return nil
	}
	return Status(status)
}

func metadata(result C.vessel_metadata_result_t) ObjectMetadata {
	data := result.metadata
	return ObjectMetadata{
		Key:            C.GoString(data.key),
		Hash:           C.GoString(data.hash),
		ETag:           C.GoString(data.etag),
		OriginalSize:   int64(data.original_size),
		CompressedSize: int64(data.compressed_size),
		Compression:    CompressionType(data.compression_type),
		CreatedAt:      time.UnixMilli(int64(result.creation_date_unix_ms)).UTC(),
		FileName:       C.GoString(data.file_name),
		Extension:      C.GoString(data.extension),
		MIMEType:       C.GoString(data.mime_type),
	}
}
