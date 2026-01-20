#pragma once

#include <mutex>
#include <atomic>

// SessionManager 统一管理所有 session 的计数
class SessionManager {
public:
    // 获取全局 session 管理器实例（单例模式）
    static SessionManager& getInstance();

    // 握手成功时调用，计数+1并打印当前总连接数
    void onHandshakeSuccess();

    // 连接关闭时调用，计数-1并打印当前总连接数
    void onConnectionClose();

    // 获取当前连接数（用于调试或查询）
    int getCount() const;

private:
    SessionManager() = default;
    ~SessionManager() = default;
    SessionManager(const SessionManager&) = delete;
    SessionManager& operator=(const SessionManager&) = delete;

    std::atomic<int> count_{0};
};
