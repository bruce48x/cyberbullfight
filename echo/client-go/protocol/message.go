package protocol

import (
	"encoding/binary"
	"errors"
	"fmt"
)

const (
	TYPE_REQUEST  = 0
	TYPE_NOTIFY   = 1
	TYPE_RESPONSE = 2
	TYPE_PUSH     = 3
)

// Message represents a Pinus protocol message
type Message struct {
	ID            uint32
	Type          byte
	CompressRoute bool
	Route         string
	Body          []byte
}

const (
	PKG_HEAD_BYTES       = 4
	MSG_FLAG_BYTES       = 1
	MSG_ROUTE_CODE_BYTES = 2
	MSG_ID_MAX_BYTES     = 5
	MSG_ROUTE_LEN_BYTES  = 1
)

const MSG_ROUTE_CODE_MAX = 0xffff

const MSG_COMPRESS_ROUTE_MASK = 0x1
const MSG_COMPRESS_GZIP_MASK = 0x1
const MSG_COMPRESS_GZIP_ENCODE_MASK = 1 << 4
const MSG_TYPE_MASK = 0x7

// EncodeMessage encodes a message to bytes
// If compressRoute is true, route should be a 2-byte string representing uint16 (big-endian)
func EncodeMessage(id uint32, msgType byte, compressRoute bool, route string, compressedRoute uint16, body []byte) ([]byte, error) {
	var routeBytes []byte
	var routeLen int

	if compressRoute {
		// Compressed route should be exactly 2 bytes (uint16)
		routeBytes = []byte{byte(compressedRoute >> 8), byte(compressedRoute)}
		routeLen = 2
	} else {
		routeBytes = []byte(route)
		routeLen = len(routeBytes)
	}

	// Calculate total size
	// id(4) + type(1) + routeLength(1) + route + body
	totalSize := 4 + 1 + 1 + routeLen + len(body)
	buf := make([]byte, totalSize)
	offset := 0

	var err error
	offset, err = encodeMsgFlag(msgType, compressRoute, buf, offset, false)
	if err != nil {
		return nil, err
	}

	if msgHasId(msgType) {
		offset, err = encodeMsgId(id, buf, offset)
		if err != nil {
			return nil, err
		}
	}

	// Write id (4 bytes, big-endian)
	// binary.BigEndian.PutUint32(buf[offset:], id)
	// offset += 4

	if msgHasRoute(msgType) {
		offset, err = encodeMsgRoute(compressRoute, route, compressedRoute, buf, offset)
		if err != nil {
			return nil, err
		}
	}
	// // Write type
	// buf[offset] = msgType
	// offset += 1

	// // Write route length and compress flag
	// if compressRoute {
	// 	buf[offset] = byte(routeLen) | 0x80 // Set high bit for compress flag
	// } else {
	// 	buf[offset] = byte(routeLen)
	// }
	// offset += 1

	// // Write route
	// if routeLen > 0 {
	// 	copy(buf[offset:], routeBytes)
	// 	offset += routeLen
	// }

	// Write body
	if len(body) > 0 {
		copy(buf[offset:], body)
		offset += len(body)
	}

	return buf, nil
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

func encodeMsgFlag(msgType byte, compressRoute bool, buffer []byte, offset int, compressGzip bool) (int, error) {
	if msgType != TYPE_REQUEST && msgType != TYPE_NOTIFY &&
		msgType != TYPE_RESPONSE && msgType != TYPE_PUSH {
		return offset, fmt.Errorf("unknown message type: %d", msgType)
	}

	if offset < 0 || offset >= len(buffer) {
		return offset, fmt.Errorf("offset out of range")
	}

	flag := msgType << 1
	if compressRoute {
		flag |= 0x01
	}
	if compressGzip {
		flag |= MSG_COMPRESS_GZIP_ENCODE_MASK
	}

	buffer[offset] = flag
	return offset + MSG_FLAG_BYTES, nil
}

func msgHasId(msgType byte) bool {
	return msgType == TYPE_REQUEST || msgType == TYPE_RESPONSE
}

func encodeMsgId(id uint32, buffer []byte, offset int) (int, error) {
	for {
		if offset >= len(buffer) {
			return offset, fmt.Errorf("offset out of range")
		}

		tmp := byte(id % 128)
		next := id / 128

		if next != 0 {
			tmp += 128 // set continuation bit
		}

		buffer[offset] = tmp
		offset++

		id = next
		if id == 0 {
			break
		}
	}
	return offset, nil
}

func msgHasRoute(msgType byte) bool {
	return msgType == TYPE_REQUEST || msgType == TYPE_NOTIFY || msgType == TYPE_PUSH
}

func encodeMsgRoute(compressRoute bool, route interface{}, compressedRoute uint16, buffer []byte, offset int) (int, error) {
	if compressRoute {
		code := int(compressedRoute)
		if code < 0 || code > MSG_ROUTE_CODE_MAX {
			return offset, fmt.Errorf("route number is overflow")
		}
		if offset+2 > len(buffer) {
			return offset, fmt.Errorf("buffer too small for compressed route")
		}

		buffer[offset] = byte((code >> 8) & 0xff)
		buffer[offset+1] = byte(code & 0xff)
		offset += 2
		return offset, nil
	}

	// uncompressed: route as []byte or string (or nil)
	var b []byte
	switch v := route.(type) {
	case nil:
		b = nil
	case []byte:
		b = v
	case string:
		b = []byte(v)
	default:
		return offset, fmt.Errorf("invalid route type for uncompressed route")
	}

	if b != nil {
		if offset+1+len(b) > len(buffer) {
			return offset, fmt.Errorf("buffer too small for route bytes")
		}
		// Note: protocol uses 1-byte length; mirror TS behavior using length & 0xff
		buffer[offset] = byte(len(b) & 0xff)
		offset++
		copy(buffer[offset:], b)
		offset += len(b)
	} else {
		if offset+1 > len(buffer) {
			return offset, fmt.Errorf("buffer too small for zero-length route")
		}
		buffer[offset] = 0
		offset++
	}

	return offset, nil
}
