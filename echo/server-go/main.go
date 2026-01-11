package main

import (
	"log"
	"net"
	"os"
	"os/signal"
	"syscall"

	"server-go/protocol"
	"server-go/session"
)

func main() {
	port := ":3010"
	listener, err := net.Listen("tcp", "0.0.0.0"+port)
	if err != nil {
		log.Fatalf("Failed to listen on port %s: %v", port, err)
	}
	defer listener.Close()

	log.Printf("[main] Server listening on port %s", port)

	// Handle graceful shutdown
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)

	go func() {
		<-sigChan
		log.Println("[main] Shutting down server...")
		listener.Close()
		os.Exit(0)
	}()

	// Register handlers
	session.RegisterHandler("connector.entryHandler.hello", func(s *session.Session, body map[string]interface{}) map[string]interface{} {
		// log.Printf("[handler] hello called. route: %s, body: %v", route, body)
		s.ReqId++
		body["serverReqId"] = s.ReqId
		return map[string]interface{}{
			"code": 0,
			"msg":  body,
		}
	})

	for {
		conn, err := listener.Accept()
		if err != nil {
			log.Printf("[main] Failed to accept connection: %v", err)
			continue
		}

		log.Printf("[main] Client connected: %s", conn.RemoteAddr())
		sess := session.NewSession(conn)
		go sess.Start()
	}
}

func init() {
	// Initialize protocol
	_ = protocol.Package{}
}
