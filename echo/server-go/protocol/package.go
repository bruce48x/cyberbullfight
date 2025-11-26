package protocol

const (
	PackageTypeHandshake    = 1
	PackageTypeHandshakeAck = 2
	PackageTypeHeartbeat    = 3
	PackageTypeData         = 4
	PackageTypeKick         = 5
)

type Package struct {
	Type   byte
	Length int
	Body   []byte
}

func PackageEncode(pkgType byte, body []byte) []byte {
	bodyLen := 0
	if body != nil {
		bodyLen = len(body)
	}

	result := make([]byte, 4+bodyLen)
	result[0] = pkgType
	result[1] = byte((bodyLen >> 16) & 0xFF)
	result[2] = byte((bodyLen >> 8) & 0xFF)
	result[3] = byte(bodyLen & 0xFF)

	if bodyLen > 0 {
		copy(result[4:], body)
	}

	return result
}

func PackageDecode(data []byte) *Package {
	if len(data) < 4 {
		return nil
	}

	pkgType := data[0]
	length := (int(data[1]) << 16) | (int(data[2]) << 8) | int(data[3])

	if len(data) < 4+length {
		return nil
	}

	body := data[4 : 4+length]

	return &Package{
		Type:   pkgType,
		Length: length,
		Body:   body,
	}
}

