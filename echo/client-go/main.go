package main

import (
	"fmt"
	"log"
	"math/rand"
	"os"
	"time"

	"client-go/client"
)

func main() {
	// Generate random user ID
	rand.Seed(time.Now().UnixNano())
	userId := generateRandomID()

	// Create client
	opts := client.ClientOptions{
		Host:   getEnv("HOST", "127.0.0.1"),
		Port:   getIntEnv("PORT", 3010),
		UserId: userId,
	}

	cli := client.NewPinusTcpClient(opts)

	// Connect
	if err := cli.Connect(); err != nil {
		log.Fatalf("Failed to connect: %v", err)
	}
	log.Printf("Connected with userId: %s", userId)

	// Send periodic requests
	ticker := time.NewTicker(1 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-ticker.C:
			msg := map[string]interface{}{
				"data": "world",
			}
			res, err := cli.Request("connector.entryHandler.hello", msg)
			if err != nil {
				log.Printf("Request failed: %v", err)
			} else {
				log.Printf("userId: %s, 发送: %v, 收到响应: %v", userId, msg, res)
			}
		}
	}
}

func generateRandomID() string {
	const charset = "abcdefghijklmnopqrstuvwxyz0123456789"
	b := make([]byte, 13)
	for i := range b {
		b[i] = charset[rand.Intn(len(charset))]
	}
	return string(b)
}

func getEnv(key, defaultValue string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return defaultValue
}

func getIntEnv(key string, defaultValue int) int {
	if value := os.Getenv(key); value != "" {
		var result int
		if _, err := fmt.Sscanf(value, "%d", &result); err == nil {
			return result
		}
	}
	return defaultValue
}

