package protocol

import (
	"errors"
)

const (
	HEAD_SIZE = 4

	TYPE_HANDSHAKE     = 1
	TYPE_HANDSHAKE_ACK = 2
	TYPE_HEARTBEAT     = 3
	TYPE_DATA          = 4
	TYPE_KICK          = 5
)

// Package represents a Pinus protocol package
type Package struct {
	Type byte
	Body []byte
}

// Encode encodes a package to bytes
func EncodePackage(pkgType byte, body []byte) []byte {
	bodyLen := len(body)
	buf := make([]byte, HEAD_SIZE+bodyLen)
	buf[0] = pkgType

	// Write body length in 3 bytes (big-endian)
	buf[1] = byte((bodyLen >> 16) & 0xFF)
	buf[2] = byte((bodyLen >> 8) & 0xFF)
	buf[3] = byte(bodyLen & 0xFF)

	if bodyLen > 0 {
		copy(buf[HEAD_SIZE:], body)
	}

	return buf
}

// Decode decodes bytes to a package
func DecodePackage(data []byte) (*Package, error) {
	if len(data) < HEAD_SIZE {
		return nil, errors.New("package too short")
	}

	pkgType := data[0]
	bodyLen := int(data[1])<<16 | int(data[2])<<8 | int(data[3])

	if bodyLen < 0 {
		return nil, errors.New("invalid body size")
	}

	if len(data) < HEAD_SIZE+bodyLen {
		return nil, errors.New("incomplete package")
	}

	var body []byte
	if bodyLen > 0 {
		body = make([]byte, bodyLen)
		copy(body, data[HEAD_SIZE:HEAD_SIZE+bodyLen])
	}

	return &Package{
		Type: pkgType,
		Body: body,
	}, nil
}

// CheckTypeData checks if the type is valid
func CheckTypeData(pkgType byte) bool {
	return pkgType == TYPE_HANDSHAKE ||
		pkgType == TYPE_HANDSHAKE_ACK ||
		pkgType == TYPE_HEARTBEAT ||
		pkgType == TYPE_DATA ||
		pkgType == TYPE_KICK
}

// HeadHandler extracts body length from head buffer
func HeadHandler(headBuffer []byte) int {
	if len(headBuffer) < HEAD_SIZE {
		return -1
	}
	return int(headBuffer[1])<<16 | int(headBuffer[2])<<8 | int(headBuffer[3])
}

// StrEncode encodes string to bytes (UTF-8)
func StrEncode(s string) []byte {
	return []byte(s)
}

// StrDecode decodes bytes to string (UTF-8)
func StrDecode(data []byte) string {
	return string(data)
}

