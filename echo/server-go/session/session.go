package session

import (
	"encoding/json"
	"log"
	"net"
	"sync"
	"time"

	"server-go/protocol"
)

type ConnectionState int

const (
	StateInited ConnectionState = iota
	StateWaitAck
	StateWorking
	StateClosed
)

type RouteHandler func(s *Session, body map[string]interface{}) map[string]interface{}

var (
	handlers     = make(map[string]RouteHandler)
	handlersLock sync.RWMutex
)

func RegisterHandler(route string, handler RouteHandler) {
	handlersLock.Lock()
	defer handlersLock.Unlock()
	handlers[route] = handler
}

type Session struct {
	conn              net.Conn
	state             ConnectionState
	heartbeatInterval time.Duration
	heartbeatTimeout  time.Duration
	lastHeartbeat     time.Time
	heartbeatSeq      int
	closeChan         chan struct{}
	mu                sync.Mutex
	ReqId             int // 记录总共收到多少次请求
}

func NewSession(conn net.Conn) *Session {
	return &Session{
		conn:      conn,
		state:     StateInited,
		closeChan: make(chan struct{}),
		ReqId:     0,
	}
}

func (s *Session) Start() {
	defer s.Close()

	buf := make([]byte, 4096)
	var dataBuf []byte

	for {
		select {
		case <-s.closeChan:
			return
		default:
		}

		s.conn.SetReadDeadline(time.Now().Add(60 * time.Second))
		n, err := s.conn.Read(buf)
		if err != nil {
			log.Printf("[session] Read error: %v", err)
			return
		}

		dataBuf = append(dataBuf, buf[:n]...)

		// Process complete packages
		for len(dataBuf) >= 4 {
			pkgLen := (int(dataBuf[1]) << 16) | (int(dataBuf[2]) << 8) | int(dataBuf[3])
			totalLen := 4 + pkgLen

			if len(dataBuf) < totalLen {
				break
			}

			pkg := protocol.PackageDecode(dataBuf[:totalLen])
			if pkg != nil {
				s.processPackage(pkg)
			}
			dataBuf = dataBuf[totalLen:]
		}
	}
}

func (s *Session) processPackage(pkg *protocol.Package) {
	switch pkg.Type {
	case protocol.PackageTypeHandshake:
		s.handleHandshake(pkg.Body)
	case protocol.PackageTypeHandshakeAck:
		s.handleHandshakeAck()
	case protocol.PackageTypeHeartbeat:
		s.handleHeartbeat()
	case protocol.PackageTypeData:
		s.handleData(pkg.Body)
	case protocol.PackageTypeKick:
		s.Close()
	}
}

func (s *Session) handleHandshake(body []byte) {
	// Prepare handshake response
	response := map[string]interface{}{
		"code": 200,
		"sys": map[string]interface{}{
			"heartbeat": 10,
			"dict":      map[string]interface{}{},
			"protos": map[string]interface{}{
				"client": map[string]interface{}{},
				"server": map[string]interface{}{},
			},
		},
		"user": map[string]interface{}{},
	}

	responseBody, _ := json.Marshal(response)
	responsePkg := protocol.PackageEncode(protocol.PackageTypeHandshake, responseBody)
	s.send(responsePkg)

	s.mu.Lock()
	s.state = StateWaitAck
	s.heartbeatInterval = 10 * time.Second
	s.heartbeatTimeout = 20 * time.Second
	s.mu.Unlock()
}

func (s *Session) handleHandshakeAck() {
	s.mu.Lock()
	s.state = StateWorking
	s.lastHeartbeat = time.Now()
	s.mu.Unlock()

	// Start heartbeat
	go s.heartbeatLoop()
}

func (s *Session) handleHeartbeat() {
	s.mu.Lock()
	s.lastHeartbeat = time.Now()
	s.mu.Unlock()

	// Send heartbeat response
	heartbeatPkg := protocol.PackageEncode(protocol.PackageTypeHeartbeat, nil)
	s.send(heartbeatPkg)
}

func (s *Session) handleData(body []byte) {
	s.mu.Lock()
	s.lastHeartbeat = time.Now()
	s.mu.Unlock()

	msg := protocol.MessageDecode(body)
	if msg == nil {
		log.Printf("[session] Failed to decode message")
		return
	}

	// Parse body as JSON
	var msgBody map[string]interface{}
	if len(msg.Body) > 0 {
		json.Unmarshal(msg.Body, &msgBody)
	}

	if msg.Type == protocol.MessageTypeRequest {
		s.handleRequest(msg.ID, msg.Route, msgBody)
	} else if msg.Type == protocol.MessageTypeNotify {
		log.Printf("[session] Notify received: route=%s, body=%v", msg.Route, msgBody)
	}
}

func (s *Session) handleRequest(id int, route string, body map[string]interface{}) {
	var responseBody map[string]interface{}

	handlersLock.RLock()
	handler, ok := handlers[route]
	handlersLock.RUnlock()

	if ok {
		responseBody = handler(s, body)
	} else {
		log.Printf("[session] Unknown route: %s", route)
		responseBody = map[string]interface{}{
			"code": 404,
			"msg":  "Route not found: " + route,
		}
	}

	responseBodyBytes, _ := json.Marshal(responseBody)
	responseMsg := protocol.MessageEncode(id, protocol.MessageTypeResponse, false, "", responseBodyBytes)
	responsePkg := protocol.PackageEncode(protocol.PackageTypeData, responseMsg)
	s.send(responsePkg)
}

func (s *Session) heartbeatLoop() {
	ticker := time.NewTicker(s.heartbeatInterval)
	defer ticker.Stop()

	for {
		select {
		case <-s.closeChan:
			return
		case <-ticker.C:
			s.mu.Lock()
			state := s.state
			lastHB := s.lastHeartbeat
			s.mu.Unlock()

			if state != StateWorking {
				return
			}

			// Check timeout
			if time.Since(lastHB) > s.heartbeatTimeout {
				log.Printf("[session] Heartbeat timeout")
				s.Close()
				return
			}

			// Send heartbeat
			heartbeatPkg := protocol.PackageEncode(protocol.PackageTypeHeartbeat, nil)
			s.send(heartbeatPkg)
		}
	}
}

func (s *Session) send(data []byte) {
	s.conn.Write(data)
}

func (s *Session) Close() {
	s.mu.Lock()
	if s.state == StateClosed {
		s.mu.Unlock()
		return
	}
	s.state = StateClosed
	s.mu.Unlock()

	close(s.closeChan)
	s.conn.Close()
	log.Printf("[session] Connection closed")
}
