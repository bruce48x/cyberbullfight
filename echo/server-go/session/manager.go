package session

import (
	"log"
	"sync"
)

var (
	manager     *SessionManager
	managerOnce sync.Once
)

// SessionManager 统一管理所有 session 的计数
type SessionManager struct {
	count int
	mu    sync.Mutex
}

// GetManager 获取全局 session 管理器实例（单例模式）
func GetManager() *SessionManager {
	managerOnce.Do(func() {
		manager = &SessionManager{
			count: 0,
		}
	})
	return manager
}

// OnHandshakeSuccess 握手成功时调用，计数+1并打印当前总连接数
func (sm *SessionManager) OnHandshakeSuccess() {
	sm.mu.Lock()
	sm.count++
	currentCount := sm.count
	sm.mu.Unlock()

	log.Printf("[session-manager] Handshake success, total connections: %d", currentCount)
}

// OnConnectionClose 连接关闭时调用，计数-1并打印当前总连接数
func (sm *SessionManager) OnConnectionClose() {
	sm.mu.Lock()
	sm.count--
	currentCount := sm.count
	sm.mu.Unlock()

	log.Printf("[session-manager] Connection closed, total connections: %d", currentCount)
}

// GetCount 获取当前连接数（用于调试或查询）
func (sm *SessionManager) GetCount() int {
	sm.mu.Lock()
	defer sm.mu.Unlock()
	return sm.count
}
