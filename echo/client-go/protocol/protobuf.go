package protocol

import (
	"encoding/json"
	"strings"
)

// Protobuf handles protobuf encoding/decoding
// For now, we'll use JSON as fallback since we don't have the actual proto definitions
type Protobuf struct {
	encoderProtos map[string]interface{}
	decoderProtos map[string]interface{}
	dict          map[string]uint16
	abbrs         map[uint16]string
}

// NewProtobuf creates a new Protobuf instance
func NewProtobuf(encoderProtos, decoderProtos map[string]interface{}) *Protobuf {
	return &Protobuf{
		encoderProtos: encoderProtos,
		decoderProtos: decoderProtos,
		dict:          make(map[string]uint16),
		abbrs:         make(map[uint16]string),
	}
}

// NormalizeRoute normalizes a route string
func (p *Protobuf) NormalizeRoute(route string) string {
	// Remove handler prefix if exists
	if idx := strings.Index(route, "."); idx != -1 {
		return route[idx+1:]
	}
	return route
}

// Check checks if a route exists in protos
func (p *Protobuf) Check(side string, route string) bool {
	normalized := p.NormalizeRoute(route)
	if side == "server" {
		_, ok := p.encoderProtos[normalized]
		return ok
	} else if side == "client" {
		_, ok := p.decoderProtos[normalized]
		return ok
	}
	return false
}

// Encode encodes a message using protobuf or JSON fallback
func (p *Protobuf) Encode(route string, msg interface{}) ([]byte, error) {
	if p.encoderProtos != nil && p.Check("server", route) {
		// If protobuf proto exists, use it
		// For now, fallback to JSON since we don't have proto definitions
		return json.Marshal(msg)
	}
	// Fallback to JSON
	return json.Marshal(msg)
}

// Decode decodes a message using protobuf or JSON fallback
func (p *Protobuf) Decode(route string, data []byte) (interface{}, error) {
	if p.decoderProtos != nil && p.Check("client", route) {
		// If protobuf proto exists, use it
		// For now, fallback to JSON since we don't have proto definitions
		var result interface{}
		err := json.Unmarshal(data, &result)
		return result, err
	}
	// Fallback to JSON
	var result interface{}
	err := json.Unmarshal(data, &result)
	return result, err
}

// SetDict sets the route dictionary for compression
func (p *Protobuf) SetDict(dict map[string]uint16) {
	p.dict = dict
	p.abbrs = make(map[uint16]string)
	for route, abbr := range dict {
		p.abbrs[abbr] = route
	}
}

// CompressRoute compresses a route using dictionary
func (p *Protobuf) CompressRoute(route string) (uint16, bool) {
	if p.dict != nil {
		if abbr, ok := p.dict[route]; ok {
			return abbr, true
		}
	}
	return 0, false
}

// DecompressRoute decompresses a route using dictionary
func (p *Protobuf) DecompressRoute(abbr uint16) (string, bool) {
	if p.abbrs != nil {
		if route, ok := p.abbrs[abbr]; ok {
			return route, true
		}
	}
	return "", false
}

