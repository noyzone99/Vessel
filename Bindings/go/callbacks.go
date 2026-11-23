package vessel

// #include "bridge.h"
import "C"

import (
	"fmt"
	"io"
	"runtime/cgo"
	"unsafe"
)

type uploadReader struct {
	reader     io.Reader
	err        error
	eof        bool
	panicked   bool
	panicValue any
}

//export vesselGoRead
func vesselGoRead(context C.uintptr_t, buffer *C.uint8_t, capacity C.int32_t) (result C.int32_t) {
	state := cgo.Handle(context).Value().(*uploadReader)
	if state.eof {
		return 0
	}
	completed := false
	defer func() {
		// Let Vessel cancel and release the native upload before propagating a panic.
		if !completed {
			state.panicked = true
			state.panicValue = recover()
			result = C.VESSEL_ERR_CANCELLED
		}
	}()
	data := unsafe.Slice((*byte)(unsafe.Pointer(buffer)), int(capacity))
	result = state.read(data)
	completed = true
	return result
}

func (state *uploadReader) read(data []byte) C.int32_t {
	for range 100 {
		count, err := state.reader.Read(data)
		if count < 0 || count > len(data) {
			state.err = fmt.Errorf("vessel: reader returned an invalid byte count: %d", count)
			return C.VESSEL_ERR_CANCELLED
		}
		if err == io.EOF {
			state.eof = true
			return C.int32_t(count)
		}
		if err != nil {
			state.err = err
			return C.VESSEL_ERR_CANCELLED
		}
		if count != 0 {
			return C.int32_t(count)
		}
	}
	state.err = io.ErrNoProgress
	return C.VESSEL_ERR_CANCELLED
}

//export vesselGoAddKey
func vesselGoAddKey(context C.uintptr_t, key *C.char) C.int32_t {
	keys := cgo.Handle(context).Value().(*[]string)
	*keys = append(*keys, C.GoString(key))
	return 0
}
