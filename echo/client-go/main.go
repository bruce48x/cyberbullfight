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
		Host:   getEnv("SERVER_HOST", "127.0.0.1"),
		Port:   getIntEnv("SERVER_PORT", 3010),
		UserId: userId,
	}

	cli := client.NewPinusTcpClient(opts)

	// Connect with retry (max 10 attempts, 5 second interval)
	maxRetries := 10
	retryInterval := 5 * time.Second
	var connected bool
	for i := 0; i < maxRetries; i++ {
		if err := cli.Connect(); err != nil {
			log.Printf("Connection attempt %d/%d failed: %v", i+1, maxRetries, err)
			if i < maxRetries-1 {
				log.Printf("Retrying in %v...", retryInterval)
				time.Sleep(retryInterval)
			}
		} else {
			connected = true
			break
		}
	}
	if !connected {
		log.Fatalf("Failed to connect after %d attempts", maxRetries)
	}
	log.Printf("Connected with userId: %s", userId)

	// Send periodic requests
	ticker := time.NewTicker(1 * time.Second)
	defer ticker.Stop()

	for range ticker.C {
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
