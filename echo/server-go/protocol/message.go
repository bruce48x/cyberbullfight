package protocol

const (
	MessageTypeRequest  = 0
	MessageTypeNotify   = 1
	MessageTypeResponse = 2
	MessageTypePush     = 3
)

type Message struct {
	ID            int
	Type          int
	CompressRoute bool
	Route         string
	Body          []byte
	CompressGzip  bool
}

func MessageEncode(id int, msgType int, compressRoute bool, route string, body []byte) []byte {
	var result []byte

	// Encode flag: type(3 bits) << 1 | compressRoute(1 bit)
	flag := byte(msgType << 1)
	if compressRoute {
		flag |= 1
	}
	result = append(result, flag)

	// Encode id (base128, only for REQUEST/RESPONSE)
	if msgType == MessageTypeRequest || msgType == MessageTypeResponse {
		idVal := id
		for {
			tmp := idVal % 128
			next := idVal / 128
			if next != 0 {
				tmp += 128
			}
			result = append(result, byte(tmp))
			idVal = next
			if idVal == 0 {
				break
			}
		}
	}

	// Encode route (only for REQUEST/NOTIFY/PUSH)
	if msgType == MessageTypeRequest || msgType == MessageTypeNotify || msgType == MessageTypePush {
		if compressRoute {
			// Compressed route: 2 bytes (big-endian)
			routeNum := 0 // Assuming route is a number when compressed
			result = append(result, byte((routeNum>>8)&0xFF))
			result = append(result, byte(routeNum&0xFF))
		} else {
			// Full route string: 1 byte length + route string
			routeBytes := []byte(route)
			result = append(result, byte(len(routeBytes)))
			result = append(result, routeBytes...)
		}
	}

	// Encode body
	if body != nil {
		result = append(result, body...)
	}

	return result
}

func MessageDecode(data []byte) *Message {
	if len(data) < 1 {
		return nil
	}

	offset := 0

	// Parse flag (1 byte)
	flag := data[offset]
	offset++

	compressRoute := (flag & 0x1) == 1
	msgType := int((flag >> 1) & 0x7)
	compressGzip := ((flag >> 4) & 0x1) == 1

	// Parse id (base128 encoded, only for REQUEST/RESPONSE)
	id := 0
	if msgType == MessageTypeRequest || msgType == MessageTypeResponse {
		i := 0
		for {
			if offset >= len(data) {
				return nil
			}
			m := data[offset]
			id += int(m&0x7F) << (7 * i)
			offset++
			i++
			if m < 128 {
				break
			}
		}
	}

	// Parse route (only for REQUEST/NOTIFY/PUSH)
	var route string
	if msgType == MessageTypeRequest || msgType == MessageTypeNotify || msgType == MessageTypePush {
		if compressRoute {
			// Compressed route: 2 bytes (big-endian)
			if offset+2 > len(data) {
				return nil
			}
			// routeNum := (int(data[offset]) << 8) | int(data[offset+1])
			// route = strconv.Itoa(routeNum)
			offset += 2
		} else {
			// Full route string: 1 byte length + route string
			if offset >= len(data) {
				return nil
			}
			routeLen := int(data[offset])
			offset++

			if routeLen > 0 {
				if offset+routeLen > len(data) {
					return nil
				}
				route = string(data[offset : offset+routeLen])
				offset += routeLen
			}
		}
	}

	// Read body (remaining bytes)
	var body []byte
	if offset < len(data) {
		body = data[offset:]
	}

	return &Message{
		ID:            id,
		Type:          msgType,
		CompressRoute: compressRoute,
		Route:         route,
		Body:          body,
		CompressGzip:  compressGzip,
	}
}

