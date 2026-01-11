#pragma once

#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <atomic>
#include <chrono>

#include "protocol.hpp"
#include "json.hpp"
#include "coroutine.hpp"

using json = nlohmann::json;

namespace server {

enum class ConnectionState {
    Inited,
    WaitAck,
    Working,
    Closed
};

class Session; // Forward declaration to allow use in RouteHandler

using RouteHandler = std::function<std::string(Session&, json)>;

class Session : public std::enable_shared_from_this<Session> {
public:
    static void register_handler(const std::string& route, RouteHandler handler);

    int ReqId;

    explicit Session(int socket_fd, Scheduler& scheduler);
    ~Session();

    void start();
    void close();
    bool handle_read(); // Returns false if connection should be closed

private:
    void run();
    void process_package(const protocol::Package& pkg);
    void handle_handshake(const std::vector<uint8_t>& body);
    void handle_handshake_ack();
    void handle_heartbeat();
    void handle_data(const std::vector<uint8_t>& body);
    void handle_request(int id, const std::string& route, const std::string& body);
    Task heartbeat_coroutine();
    void send(const std::vector<uint8_t>& data);

    static std::map<std::string, RouteHandler> handlers_;
    static std::mutex handlers_mutex_;

    int socket_fd_;
    Scheduler& scheduler_;
    ConnectionState state_ = ConnectionState::Inited;
    std::chrono::seconds heartbeat_interval_{10};
    std::chrono::seconds heartbeat_timeout_{20};
    std::chrono::steady_clock::time_point last_heartbeat_;
    std::atomic<bool> running_{true};
    std::mutex mutex_;
    std::vector<uint8_t> data_buf_; // Buffer for incomplete packages
    int heartbeat_timer_id_ = -1; // Timer ID for heartbeat coroutine
};

} // namespace server

