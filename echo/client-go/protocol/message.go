package protocol

import (
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
// Format: flag(1) + id(variable, base128) + route + body
// flag: type(3 bits) << 1 | compressRoute(1 bit)
// id: base128 encoded (only for REQUEST/RESPONSE)
// route: 2 bytes (big-endian) if compressed, or 1 byte length + string if not
func EncodeMessage(id uint32, msgType byte, compressRoute bool, route string, compressedRoute uint16, body []byte) ([]byte, error) {
	// Estimate buffer size: flag(1) + max_id(5) + max_route(256) + body
	maxSize := 1 + 5 + 256 + len(body)
	buf := make([]byte, 0, maxSize)
	
	// Encode flag
	flag := (msgType << 1)
	if compressRoute {
		flag |= 0x1
	}
	buf = append(buf, byte(flag))

	// Encode id (base128, only for REQUEST/RESPONSE)
	if msgHasId(msgType) {
		idBytes := encodeMsgIdToBytes(id)
		buf = append(buf, idBytes...)
	}

	// Encode route (only for REQUEST/NOTIFY/PUSH)
	if msgHasRoute(msgType) {
		var routeBytes []byte
		if compressRoute {
			// Compressed route: 2 bytes (big-endian)
			routeBytes = []byte{byte(compressedRoute >> 8), byte(compressedRoute)}
		} else {
			// Full route string: 1 byte length + route string
			routeStrBytes := []byte(route)
			if len(routeStrBytes) > 255 {
				return nil, fmt.Errorf("route string too long: %d", len(routeStrBytes))
			}
			routeBytes = append([]byte{byte(len(routeStrBytes))}, routeStrBytes...)
		}
		buf = append(buf, routeBytes...)
	}

	// Encode body
	if len(body) > 0 {
		buf = append(buf, body...)
	}

	return buf, nil
}

// encodeMsgIdToBytes encodes id using base128 encoding
func encodeMsgIdToBytes(id uint32) []byte {
	var result []byte
	for {
		tmp := byte(id % 128)
		next := id / 128
		if next != 0 {
			tmp += 128 // set continuation bit
		}
		result = append(result, tmp)
		id = next
		if id == 0 {
			break
		}
	}
	return result
}

// DecodeMessage decodes bytes to a message
// Format: flag(1) + id(variable, base128) + route + body
// flag: type(3 bits) << 1 | compressRoute(1 bit)
// id: base128 encoded (only for REQUEST/RESPONSE)
// route: 2 bytes (big-endian) if compressed, or 1 byte length + string if not
func DecodeMessage(data []byte) (*Message, error) {
	if len(data) < 1 {
		return nil, errors.New("message too short")
	}

	msg := &Message{}
	offset := 0

	// Parse flag (1 byte)
	flag := data[offset]
	offset++

	msg.CompressRoute = (flag & 0x1) != 0
	msg.Type = (flag >> 1) & 0x7

	// Parse id (base128 encoded, only for REQUEST/RESPONSE)
	if msgHasId(msg.Type) {
		id := uint32(0)
		m := uint32(0)
		i := 0
		for {
			if offset >= len(data) {
				return nil, errors.New("not enough bytes for id")
			}
			m = uint32(data[offset])
			id += (m & 0x7f) << (7 * i)
			offset++
			i++
			if m < 128 {
				break
			}
		}
		msg.ID = id
	}

	// Parse route (only for REQUEST/NOTIFY/PUSH)
	if msgHasRoute(msg.Type) {
		if msg.CompressRoute {
			// Compressed route: 2 bytes (big-endian)
			if offset+1 >= len(data) {
				return nil, errors.New("not enough bytes for compressed route")
			}
			routeCode := uint16(data[offset])<<8 | uint16(data[offset+1])
			// Store as 2-byte string for decompression later
			msg.Route = string([]byte{byte(routeCode >> 8), byte(routeCode)})
			offset += 2
		} else {
			// Full route string: 1 byte length + route string
			if offset >= len(data) {
				return nil, errors.New("not enough bytes for route length")
			}
			routeLen := int(data[offset])
			offset++

			if routeLen > 0 {
				if offset+routeLen > len(data) {
					return nil, errors.New("not enough bytes for route string")
				}
				msg.Route = string(data[offset : offset+routeLen])
				offset += routeLen
			} else {
				msg.Route = ""
			}
		}
	}

	// Read body (remaining bytes)
	if offset < len(data) {
		msg.Body = make([]byte, len(data)-offset)
		copy(msg.Body, data[offset:])
	}

	return msg, nil
}

func msgHasId(msgType byte) bool {
	return msgType == TYPE_REQUEST || msgType == TYPE_RESPONSE
}

func msgHasRoute(msgType byte) bool {
	return msgType == TYPE_REQUEST || msgType == TYPE_NOTIFY || msgType == TYPE_PUSH
}
