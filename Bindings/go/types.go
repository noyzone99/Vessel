package vessel

import (
	"fmt"
	"strings"
	"time"
)

// Options controls how Vessel opens a store.
type Options struct {
	EnableMetadataCompression bool
}

// CompressionType identifies an object's payload codec.
type CompressionType int32

const (
	None CompressionType = iota
	Zstd
	LZ4
)

// CompressionLevel controls the codec's compression effort.
type CompressionLevel int32

const (
	Balanced CompressionLevel = iota
	Fastest
	High
	Ultra
)

// WriteOptions controls replacement, compression, and optional file information.
// The zero value preserves existing keys and writes uncompressed data.
type WriteOptions struct {
	Overwrite        bool
	Compression      CompressionType
	CompressionLevel CompressionLevel
	FileName         string
	Extension        string
	MIMEType         string
}

// ObjectMetadata describes a committed object. Empty file strings indicate absent information.
type ObjectMetadata struct {
	Key            string
	Hash           string
	ETag           string
	OriginalSize   int64
	CompressedSize int64
	Compression    CompressionType
	CreatedAt      time.Time
	FileName       string
	Extension      string
	MIMEType       string
}

// ContentHash returns the SHA-256 digest of the original payload in hexadecimal.
func (m ObjectMetadata) ContentHash() string {
	digest, _, _ := strings.Cut(m.Hash, "-")
	return digest
}

// Status is a status code from the Vessel C ABI and implements error.
type Status int32

const (
	ErrUnknown          Status = -1
	ErrInvalidArgument  Status = -2
	ErrNotFound         Status = -3
	ErrAlreadyExists    Status = -4
	ErrIO               Status = -5
	ErrDisposed         Status = -6
	ErrInvalidOperation Status = -7
	ErrCancelled        Status = -8
	ErrLimitExceeded    Status = -9
	ErrOutOfMemory      Status = -10
)

func (s Status) Error() string {
	switch s {
	case ErrUnknown:
		return "vessel: unknown failure"
	case ErrInvalidArgument:
		return "vessel: invalid argument"
	case ErrNotFound:
		return "vessel: object not found"
	case ErrAlreadyExists:
		return "vessel: object already exists"
	case ErrIO:
		return "vessel: I/O failure"
	case ErrDisposed:
		return "vessel: handle is closed"
	case ErrInvalidOperation:
		return "vessel: invalid operation"
	case ErrCancelled:
		return "vessel: operation cancelled"
	case ErrLimitExceeded:
		return "vessel: limit exceeded"
	case ErrOutOfMemory:
		return "vessel: out of memory"
	default:
		return fmt.Sprintf("vessel: native status %d", s)
	}
}
