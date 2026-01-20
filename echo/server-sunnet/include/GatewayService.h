#pragma once

#include "Service.h"
#include "protocol.h"
#include <unordered_map>
#include <vector>
#include <chrono>
#include <mutex>
#include <string>
#include <functional>
#include <memory>
#include <atomic>
#include <thread>

// Include JSON library
#include "../3rd/json/single_include/nlohmann/json.hpp"

using json = nlohmann::json;

enum class ConnectionState {
    Inited,
    WaitAck,
    Working,
    Closed
};

struct Session {
    int fd;
    ConnectionState state = ConnectionState::Inited;
    std::vector<uint8_t> data_buf;
    int ReqId = 0;
    std::chrono::steady_clock::time_point last_heartbeat;
    std::chrono::seconds heartbeat_interval{10};
    std::chrono::seconds heartbeat_timeout{20};
};

using RouteHandler = std::function<std::string(Session&, json)>;

class GatewayService : public Service {
public:
    GatewayService();
    ~GatewayService();

    void OnInit() override;
    void OnMsg(shared_ptr<BaseMsg> msg) override;
    void OnExit() override;

    static void register_handler(const std::string& route, RouteHandler handler);
    static void register_service(); // 注册 GatewayService 到服务工厂

private:
    void OnAcceptMsg(shared_ptr<SocketAcceptMsg> msg) override;
    void OnRWMsg(shared_ptr<SocketRWMsg> msg) override;
    void OnSocketData(int fd, const char *buff, int len) override;
    void OnSocketWritable(int fd) override;
    void OnSocketClose(int fd) override;

    void process_package(int fd, const protocol::Package& pkg);
    void handle_handshake(int fd, const std::vector<uint8_t>& body);
    void handle_handshake_ack(int fd);
    void handle_heartbeat(int fd);
    void handle_data(int fd, const std::vector<uint8_t>& body);
    void handle_request(int fd, int id, const std::string& route, const std::string& body);
    void send(int fd, const std::vector<uint8_t>& data);
    void send_heartbeat(int fd);
    void check_heartbeat_timeout();

    std::unordered_map<int, std::shared_ptr<Session>> sessions_;
    std::mutex sessions_mutex_;

    static std::unordered_map<std::string, RouteHandler> handlers_;
    static std::mutex handlers_mutex_;

    std::atomic<bool> heartbeat_running_{true};
    std::thread heartbeat_thread_;
};
