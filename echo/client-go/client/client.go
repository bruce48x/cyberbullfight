package client

import (
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net"
	"sync"
	"time"

	"client-go/protocol"
)

const (
	NetStateInited  = 0
	NetStateWaitAck = 1
	NetStateWorking = 2
	NetStateClosed  = 3
)

const (
	ResponseOK        = 200
	ResponseFail      = 500
	ResponseOldClient = 501
)

const (
	ReadStateHead   = 0
	ReadStateBody   = 1
	ReadStateClosed = 2
)

const gapThreshold = 100 // heartbeat gap threshold (ms)

type HandshakeData struct {
	Sys struct {
		Type    string                 `json:"type"`
		Version string                 `json:"version"`
		RSA     map[string]interface{} `json:"rsa"`
		Dict    map[string]uint16      `json:"dict"`
		Protos  map[string]interface{} `json:"protos"`
	} `json:"sys"`
	User map[string]interface{} `json:"user"`
}

type HandshakeResponse struct {
	Code int                    `json:"code"`
	Sys  map[string]interface{} `json:"sys"`
	User map[string]interface{} `json:"user"`
}

type PinusTcpClient struct {
	host     string
	port     int
	userId   string
	conn     net.Conn
	netState int

	// Read state
	readState     int
	headBuffer    []byte
	headOffset    int
	packageBuffer []byte
	packageOffset int
	packageSize   int

	// Heartbeat
	heartbeatInterval     time.Duration
	heartbeatTimeout      time.Duration
	heartbeatTimer        *time.Timer
	heartbeatTimeoutTimer *time.Timer
	nextHeartbeatTimeout  time.Time

	// Request/Response
	reqId         uint32
	callbacks     map[uint32]func(interface{})
	callbackMutex sync.Mutex

	// Protocol
	dict     map[string]uint16
	abbrs    map[uint16]string
	protos   map[string]interface{}
	protobuf *protocol.Protobuf

	// Events
	handshakeChan chan *HandshakeResponse
	messageChan   chan *protocol.Message
	errorChan     chan error
}

type ClientOptions struct {
	Host       string
	Port       int
	UserId     string
	TcpEncrypt bool
}

func NewPinusTcpClient(opts ClientOptions) *PinusTcpClient {
	return &PinusTcpClient{
		host:          opts.Host,
		port:          opts.Port,
		userId:        opts.UserId,
		netState:      NetStateInited,
		readState:     ReadStateHead,
		headBuffer:    make([]byte, protocol.HEAD_SIZE),
		headOffset:    0,
		callbacks:     make(map[uint32]func(interface{})),
		handshakeChan: make(chan *HandshakeResponse, 1),
		messageChan:   make(chan *protocol.Message, 100),
		errorChan:     make(chan error, 10),
	}
}

func (c *PinusTcpClient) Connect() error {
	address := fmt.Sprintf("%s:%d", c.host, c.port)
	conn, err := net.Dial("tcp", address)
	if err != nil {
		return fmt.Errorf("failed to connect: %w", err)
	}

	c.conn = conn
	c.netState = NetStateInited

	// Send handshake
	handshakeData := HandshakeData{}
	handshakeData.Sys.Type = "client-simulator"
	handshakeData.Sys.Version = "0.1.0"
	handshakeData.Sys.RSA = make(map[string]interface{})
	handshakeData.User = make(map[string]interface{})

	handshakeJSON, _ := json.Marshal(handshakeData)
	handshakeBody := protocol.StrEncode(string(handshakeJSON))
	handshakePkg := protocol.EncodePackage(protocol.TYPE_HANDSHAKE, handshakeBody)

	if _, err := c.conn.Write(handshakePkg); err != nil {
		return fmt.Errorf("failed to send handshake: %w", err)
	}

	// Start reading
	go c.readLoop()

	// Wait for handshake response
	select {
	case resp := <-c.handshakeChan:
		if resp.Code == ResponseOldClient {
			return fmt.Errorf("client version not fulfill")
		}
		if resp.Code != ResponseOK {
			return fmt.Errorf("handshake fail: code=%d", resp.Code)
		}
		c.handleHandshakeResponse(resp)

		// Send handshake ack
		ackPkg := protocol.EncodePackage(protocol.TYPE_HANDSHAKE_ACK, nil)
		if _, err := c.conn.Write(ackPkg); err != nil {
			return fmt.Errorf("failed to send handshake ack: %w", err)
		}
		c.netState = NetStateWorking
		c.startHeartbeat()
		return nil
	case err := <-c.errorChan:
		return err
	case <-time.After(10 * time.Second):
		return fmt.Errorf("handshake timeout")
	}
}

func (c *PinusTcpClient) handleHandshakeResponse(resp *HandshakeResponse) {
	if resp.Sys != nil {
		// Handle heartbeat interval
		if heartbeat, ok := resp.Sys["heartbeat"].(float64); ok {
			c.heartbeatInterval = time.Duration(heartbeat) * time.Second
			c.heartbeatTimeout = c.heartbeatInterval * 2
		}

		// Handle dict
		if dictData, ok := resp.Sys["dict"].(map[string]interface{}); ok {
			c.dict = make(map[string]uint16)
			for route, abbr := range dictData {
				if abbrNum, ok := abbr.(float64); ok {
					c.dict[route] = uint16(abbrNum)
				}
			}
			c.abbrs = make(map[uint16]string)
			for route, abbr := range c.dict {
				c.abbrs[abbr] = route
			}
		}

		// Handle protos
		if protos, ok := resp.Sys["protos"].(map[string]interface{}); ok {
			c.protos = protos
			var encoderProtos, decoderProtos map[string]interface{}
			if clientProtos, ok := protos["client"].(map[string]interface{}); ok {
				decoderProtos = clientProtos
			}
			if serverProtos, ok := protos["server"].(map[string]interface{}); ok {
				encoderProtos = serverProtos
			}
			c.protobuf = protocol.NewProtobuf(encoderProtos, decoderProtos)
			if c.dict != nil {
				c.protobuf.SetDict(c.dict)
			}
		}
	}
}

func (c *PinusTcpClient) readLoop() {
	buffer := make([]byte, 4096)
	for {
		n, err := c.conn.Read(buffer)
		if err != nil {
			if err != io.EOF {
				c.errorChan <- fmt.Errorf("read error: %w", err)
			}
			return
		}

		offset := 0
		for offset < n && c.readState != ReadStateClosed {
			if c.readState == ReadStateHead {
				offset = c.readHead(buffer, offset, n)
			}
			if c.readState == ReadStateBody {
				offset = c.readBody(buffer, offset, n)
			}
		}
	}
}

func (c *PinusTcpClient) readHead(data []byte, offset, totalLen int) int {
	hlen := protocol.HEAD_SIZE - c.headOffset
	dlen := totalLen - offset
	len := hlen
	if dlen < len {
		len = dlen
	}
	dend := offset + len

	copy(c.headBuffer[c.headOffset:], data[offset:dend])
	c.headOffset += len

	if c.headOffset == protocol.HEAD_SIZE {
		// Head finished
		size := protocol.HeadHandler(c.headBuffer)
		if size < 0 {
			c.errorChan <- fmt.Errorf("invalid body size: %d", size)
			c.readState = ReadStateClosed
			return totalLen
		}

		if !protocol.CheckTypeData(c.headBuffer[0]) {
			log.Printf("close the connection with invalid head message")
			c.readState = ReadStateClosed
			return totalLen
		}

		c.packageSize = size + protocol.HEAD_SIZE
		c.packageBuffer = make([]byte, c.packageSize)
		copy(c.packageBuffer, c.headBuffer)
		c.packageOffset = protocol.HEAD_SIZE
		c.readState = ReadStateBody
	}

	return dend
}

func (c *PinusTcpClient) readBody(data []byte, offset, totalLen int) int {
	blen := c.packageSize - c.packageOffset
	dlen := totalLen - offset
	len := blen
	if dlen < len {
		len = dlen
	}
	dend := offset + len

	copy(c.packageBuffer[c.packageOffset:], data[offset:dend])
	c.packageOffset += len

	if c.packageOffset == c.packageSize {
		// Package finished
		c.processPackage(c.packageBuffer)
		c.reset()
	}

	return dend
}

func (c *PinusTcpClient) reset() {
	c.headOffset = 0
	c.packageOffset = 0
	c.packageSize = 0
	c.packageBuffer = nil
	c.readState = ReadStateHead
}

func (c *PinusTcpClient) processPackage(data []byte) {
	pkg, err := protocol.DecodePackage(data)
	if err != nil {
		log.Printf("failed to decode package: %v", err)
		return
	}

	switch pkg.Type {
	case protocol.TYPE_HANDSHAKE:
		c.handleHandshake(pkg)
	case protocol.TYPE_HANDSHAKE_ACK:
		c.handleHandshakeAck(pkg)
	case protocol.TYPE_HEARTBEAT:
		c.handleHeartbeat(pkg)
	case protocol.TYPE_DATA:
		c.handleData(pkg)
	case protocol.TYPE_KICK:
		c.handleKick(pkg)
	default:
		log.Printf("unknown package type: %d", pkg.Type)
	}
}

func (c *PinusTcpClient) handleHandshake(pkg *protocol.Package) {
	if c.netState != NetStateInited {
		return
	}

	var resp HandshakeResponse
	bodyStr := protocol.StrDecode(pkg.Body)
	if err := json.Unmarshal([]byte(bodyStr), &resp); err != nil {
		log.Printf("failed to parse handshake: %v", err)
		resp.Code = ResponseFail
	}
	c.handshakeChan <- &resp
}

func (c *PinusTcpClient) handleHandshakeAck(pkg *protocol.Package) {
	if c.netState != NetStateWaitAck {
		return
	}
	c.netState = NetStateWorking
	c.triggerHeartbeat()
}

func (c *PinusTcpClient) handleHeartbeat(pkg *protocol.Package) {
	if c.netState != NetStateWorking {
		return
	}
	c.triggerHeartbeat()
}

func (c *PinusTcpClient) handleData(pkg *protocol.Package) {
	if c.netState != NetStateWorking {
		return
	}
	msg, err := protocol.DecodeMessage(pkg.Body)
	if err != nil {
		log.Printf("failed to decode message: %v", err)
		return
	}

	// Decompress route if needed
	if msg.CompressRoute && c.abbrs != nil && len(msg.Route) == 2 {
		// Route is compressed as 2 bytes (uint16, big-endian)
		routeBytes := []byte(msg.Route)
		abbr := uint16(routeBytes[0])<<8 | uint16(routeBytes[1])
		if route, ok := c.abbrs[abbr]; ok {
			msg.Route = route
		} else {
			log.Printf("failed to decompress route, abbr=%d", abbr)
		}
	}

	// Decode body
	var body interface{}
	if c.protobuf != nil {
		decoded, err := c.protobuf.Decode(msg.Route, msg.Body)
		if err == nil {
			body = decoded
		} else {
			// Fallback to JSON
			json.Unmarshal(msg.Body, &body)
		}
	} else {
		json.Unmarshal(msg.Body, &body)
	}

	if msg.Type == protocol.TYPE_PUSH {
		log.Printf("[%s] 通知 %s: %v", c.userId, msg.Route, body)
	} else if msg.Type == protocol.TYPE_RESPONSE {
		c.callbackMutex.Lock()
		cb, ok := c.callbacks[msg.ID]
		if ok {
			delete(c.callbacks, msg.ID)
		}
		c.callbackMutex.Unlock()

		if ok && cb != nil {
			cb(body)
		}
	}
}

func (c *PinusTcpClient) handleKick(pkg *protocol.Package) {
	if c.netState != NetStateWorking {
		return
	}
	var msg map[string]interface{}
	bodyStr := protocol.StrDecode(pkg.Body)
	json.Unmarshal([]byte(bodyStr), &msg)
	log.Printf("被踢: %v", msg)
}

func (c *PinusTcpClient) startHeartbeat() {
	if c.heartbeatInterval <= 0 {
		return
	}
	// Start the first heartbeat cycle
	c.scheduleNextHeartbeat()
}

// scheduleNextHeartbeat schedules the next heartbeat to be sent
func (c *PinusTcpClient) scheduleNextHeartbeat() {
	if c.heartbeatInterval <= 0 {
		return
	}

	// Stop existing heartbeat timer if any
	if c.heartbeatTimer != nil {
		c.heartbeatTimer.Stop()
		c.heartbeatTimer = nil
	}

	// Schedule next heartbeat send
	c.heartbeatTimer = time.AfterFunc(c.heartbeatInterval, func() {
		c.heartbeatTimer = nil
		// Send heartbeat
		heartbeatPkg := protocol.EncodePackage(protocol.TYPE_HEARTBEAT, nil)
		if c.conn != nil {
			c.conn.Write(heartbeatPkg)
		}
		// Set timeout check
		c.nextHeartbeatTimeout = time.Now().Add(c.heartbeatTimeout)
		c.scheduleHeartbeatTimeout()
		// Schedule next heartbeat
		c.scheduleNextHeartbeat()
	})
}

// scheduleHeartbeatTimeout schedules the heartbeat timeout check
func (c *PinusTcpClient) scheduleHeartbeatTimeout() {
	// Stop existing timeout timer if any
	if c.heartbeatTimeoutTimer != nil {
		c.heartbeatTimeoutTimer.Stop()
		c.heartbeatTimeoutTimer = nil
	}

	// Schedule timeout check
	c.heartbeatTimeoutTimer = time.AfterFunc(c.heartbeatTimeout, func() {
		c.heartbeatTimeoutTimer = nil
		// Check if we've exceeded the timeout
		gap := time.Until(c.nextHeartbeatTimeout)
		if gap > gapThreshold*time.Millisecond {
			// Reschedule for the remaining time
			c.heartbeatTimeoutTimer = time.AfterFunc(gap, func() {
				log.Printf("tcp heartbeat timeout")
			})
		} else {
			log.Printf("tcp heartbeat timeout")
		}
	})
}

// triggerHeartbeat is called when we receive a heartbeat from server
// It resets the heartbeat sending timer and timeout check timer
func (c *PinusTcpClient) triggerHeartbeat() {
	if c.heartbeatInterval <= 0 {
		return
	}

	// Reset heartbeat sending timer (clear old, schedule new)
	c.scheduleNextHeartbeat()
	
	// Reset timeout check timer (clear old, schedule new)
	c.scheduleHeartbeatTimeout()
}

func (c *PinusTcpClient) encode(route string, msg interface{}) ([]byte, error) {
	if c.protobuf != nil {
		normalized := c.protobuf.NormalizeRoute(route)
		if c.protobuf.Check("server", normalized) {
			return c.protobuf.Encode(normalized, msg)
		}
	}
	// Fallback to JSON
	return json.Marshal(msg)
}

func (c *PinusTcpClient) compressRoute(route string) (uint16, bool) {
	if c.dict != nil {
		if abbr, ok := c.dict[route]; ok {
			return abbr, true
		}
	}
	return 0, false
}

func (c *PinusTcpClient) Request(route string, msg interface{}) (interface{}, error) {
	if route == "" {
		return nil, fmt.Errorf("route cannot be empty")
	}

	c.reqId++
	reqId := c.reqId

	// Encode message
	encodedBody, err := c.encode(route, msg)
	if err != nil {
		return nil, fmt.Errorf("failed to encode message: %w", err)
	}

	// Compress route
	compressedRoute, compressRoute := c.compressRoute(route)

	// Encode message
	encodedMsg, err := protocol.EncodeMessage(reqId, protocol.TYPE_REQUEST, compressRoute, route, compressedRoute, encodedBody)
	if err != nil {
		return nil, fmt.Errorf("failed to encode message: %w", err)
	}

	// Encode package
	pkg := protocol.EncodePackage(protocol.TYPE_DATA, encodedMsg)

	// Send
	if _, err := c.conn.Write(pkg); err != nil {
		return nil, fmt.Errorf("failed to send request: %w", err)
	}

	// Wait for response
	resultChan := make(chan interface{}, 1)

	c.callbackMutex.Lock()
	c.callbacks[reqId] = func(res interface{}) {
		resultChan <- res
	}
	c.callbackMutex.Unlock()

	select {
	case result := <-resultChan:
		return result, nil
	case err := <-c.errorChan:
		return nil, err
	case <-time.After(30 * time.Second):
		c.callbackMutex.Lock()
		delete(c.callbacks, reqId)
		c.callbackMutex.Unlock()
		return nil, fmt.Errorf("request timeout")
	}
}

func (c *PinusTcpClient) Notify(route string, msg interface{}) error {
	// Encode message
	encodedBody, err := c.encode(route, msg)
	if err != nil {
		return fmt.Errorf("failed to encode message: %w", err)
	}

	// Compress route
	compressedRoute, compressRoute := c.compressRoute(route)

	// Encode message
	encodedMsg, err := protocol.EncodeMessage(0, protocol.TYPE_NOTIFY, compressRoute, route, compressedRoute, encodedBody)
	if err != nil {
		return fmt.Errorf("failed to encode message: %w", err)
	}

	// Encode package
	pkg := protocol.EncodePackage(protocol.TYPE_DATA, encodedMsg)

	// Send
	_, err = c.conn.Write(pkg)
	return err
}

func (c *PinusTcpClient) Disconnect() {
	c.netState = NetStateClosed
	if c.heartbeatTimer != nil {
		c.heartbeatTimer.Stop()
	}
	if c.heartbeatTimeoutTimer != nil {
		c.heartbeatTimeoutTimer.Stop()
	}
	if c.conn != nil {
		c.conn.Close()
		c.conn = nil
	}
}
