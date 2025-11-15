package protocol

import (
	"encoding/binary"
	"errors"
)

const (
	TYPE_REQUEST  = 0
	TYPE_NOTIFY   = 1
	TYPE_RESPONSE = 2
	TYPE_PUSH     = 3
)

// Message represents a Pinus protocol message
type Message struct {
	ID           uint32
	Type         byte
	CompressRoute bool
	Route        string
	Body         []byte
}

// EncodeMessage encodes a message to bytes
// If compressRoute is true, route should be a 2-byte string representing uint16 (big-endian)
func EncodeMessage(id uint32, msgType byte, compressRoute bool, route string, body []byte) []byte {
	var routeBytes []byte
	var routeLen int

	if compressRoute {
		// Compressed route should be exactly 2 bytes (uint16)
		routeBytes = []byte(route)
		if len(routeBytes) != 2 {
			// Fallback: treat as normal route
			routeBytes = []byte(route)
			routeLen = len(routeBytes)
			compressRoute = false
		} else {
			routeLen = 2
		}
	} else {
		routeBytes = []byte(route)
		routeLen = len(routeBytes)
	}

	// Calculate total size
	// id(4) + type(1) + routeLength(1) + route + body
	totalSize := 4 + 1 + 1 + routeLen + len(body)
	buf := make([]byte, totalSize)
	offset := 0

	// Write id (4 bytes, big-endian)
	binary.BigEndian.PutUint32(buf[offset:], id)
	offset += 4

	// Write type
	buf[offset] = msgType
	offset += 1

	// Write route length and compress flag
	if compressRoute {
		buf[offset] = byte(routeLen) | 0x80 // Set high bit for compress flag
	} else {
		buf[offset] = byte(routeLen)
	}
	offset += 1

	// Write route
	if routeLen > 0 {
		copy(buf[offset:], routeBytes)
		offset += routeLen
	}

	// Write body
	if len(body) > 0 {
		copy(buf[offset:], body)
	}

	return buf
}

// DecodeMessage decodes bytes to a message
func DecodeMessage(data []byte) (*Message, error) {
	if len(data) < 6 {
		return nil, errors.New("message too short")
	}

	msg := &Message{}
	offset := 0

	// Read id (4 bytes)
	msg.ID = binary.BigEndian.Uint32(data[offset:])
	offset += 4

	// Read type
	msg.Type = data[offset]
	offset += 1

	// Read route length and compress flag
	routeFlag := data[offset]
	msg.CompressRoute = (routeFlag & 0x80) != 0
	routeLen := int(routeFlag & 0x7F)
	offset += 1

	// Read route
	if routeLen > 0 {
		if len(data) < offset+routeLen {
			return nil, errors.New("incomplete route")
		}
		// Store route as raw bytes for decompression later
		msg.Route = string(data[offset : offset+routeLen])
		offset += routeLen
	}

	// Read body
	if offset < len(data) {
		msg.Body = make([]byte, len(data)-offset)
		copy(msg.Body, data[offset:])
	}

	return msg, nil
}

