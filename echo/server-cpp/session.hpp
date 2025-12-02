#pragma once

#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <atomic>

#include "protocol.hpp"
#include "json.hpp"

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

    explicit Session(int socket_fd);
    ~Session();

    void start();
    void close();

private:
    void run();
    void process_package(const protocol::Package& pkg);
    void handle_handshake(const std::vector<uint8_t>& body);
    void handle_handshake_ack();
    void handle_heartbeat();
    void handle_data(const std::vector<uint8_t>& body);
    void handle_request(int id, const std::string& route, const std::string& body);
    void heartbeat_loop();
    void send(const std::vector<uint8_t>& data);

    static std::map<std::string, RouteHandler> handlers_;
    static std::mutex handlers_mutex_;

    int socket_fd_;
    ConnectionState state_ = ConnectionState::Inited;
    std::chrono::seconds heartbeat_interval_{10};
    std::chrono::seconds heartbeat_timeout_{20};
    std::chrono::steady_clock::time_point last_heartbeat_;
    std::atomic<bool> running_{true};
    std::mutex mutex_;
    std::thread read_thread_;
    std::thread heartbeat_thread_;
};

} // namespace server

