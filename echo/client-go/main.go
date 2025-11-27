package main

import (
	"fmt"
	"log"
	"math/rand"
	"os"
	"os/signal"
	"sync"
	"sync/atomic"
	"syscall"
	"time"

	"client-go/client"
)

var (
	totalRequests int64
	successCount  int64
	failCount     int64
)

func printStats() {
	log.Printf("\n========== 统计信息 ==========")
	log.Printf("总请求数: %d", atomic.LoadInt64(&totalRequests))
	log.Printf("成功: %d", atomic.LoadInt64(&successCount))
	log.Printf("失败: %d", atomic.LoadInt64(&failCount))
	log.Printf("==============================\n")
}

func main() {
	rand.Seed(time.Now().UnixNano())

	// 捕获退出信号
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)
	go func() {
		<-sigChan
		printStats()
		os.Exit(0)
	}()

	count := getIntEnv("COUNT", 1)
	log.Printf("Starting %d robot(s)...", count)

	var wg sync.WaitGroup
	for i := 0; i < count; i++ {
		wg.Add(1)
		go func(index int) {
			defer wg.Done()
			runRobot(index)
		}(i + 1)
	}
	wg.Wait()
}

func runRobot(index int) {
	userId := generateRandomID()

	opts := client.ClientOptions{
		Host:   getEnv("SERVER_HOST", "127.0.0.1"),
		Port:   getIntEnv("SERVER_PORT", 3010),
		UserId: userId,
	}

	cli := client.NewPinusTcpClient(opts)

	maxRetries := 10
	retryInterval := 5 * time.Second
	var connected bool
	for i := 0; i < maxRetries; i++ {
		if err := cli.Connect(); err != nil {
			log.Printf("Robot %d connection attempt %d/%d failed: %v", index, i+1, maxRetries, err)
			if i < maxRetries-1 {
				log.Printf("Robot %d retrying in %v...", index, retryInterval)
				time.Sleep(retryInterval)
			}
		} else {
			connected = true
			break
		}
	}
	if !connected {
		log.Printf("Robot %d failed to connect after %d attempts", index, maxRetries)
		return
	}
	log.Printf("Robot %d connected with userId: %s", index, userId)

	ticker := time.NewTicker(1 * time.Second)
	defer ticker.Stop()

	reqId := 1
	for range ticker.C {
		msg := map[string]interface{}{
			"data": fmt.Sprintf("world%d", reqId),
		}
		reqId++
		atomic.AddInt64(&totalRequests, 1)
		res, err := cli.Request("connector.entryHandler.hello", msg)
		if err != nil {
			atomic.AddInt64(&failCount, 1)
			log.Printf("Robot %d request failed: %v", index, err)
		} else {
			atomic.AddInt64(&successCount, 1)
			log.Printf("Robot %d userId: %s, 发送: %v, 收到响应: %v", index, userId, msg, res)
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
