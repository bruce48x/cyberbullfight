#include "session.hpp"

#include <iostream>
#include <cstring>
#include <unistd.h>
#include <sys/socket.h>
#include <errno.h>

namespace server {

std::map<std::string, RouteHandler> Session::handlers_;
std::mutex Session::handlers_mutex_;

void Session::register_handler(const std::string& route, RouteHandler handler) {
    std::lock_guard lock(handlers_mutex_);
    handlers_[route] = std::move(handler);
}

Session::Session(int socket_fd, Scheduler& scheduler) : socket_fd_(socket_fd), scheduler_(scheduler), ReqId(0) {}

Session::~Session() {
    close();
}

void Session::start() {
    // Session is now managed by epoll, no need for separate thread
}

bool Session::handle_read() {
    std::vector<uint8_t> buffer(4096);
    
    while (running_) {
        ssize_t n = recv(socket_fd_, buffer.data(), buffer.size(), 0);
        if (n < 0) {
            if (errno == EAGAIN || errno == EWOULDBLOCK) {
                // No more data available (non-blocking)
                break;
            }
            std::cout << "[session] Recv error: " << strerror(errno) << std::endl;
            return false;
        }
        
        if (n == 0) {
            std::cout << "[session] Connection closed by client" << std::endl;
            return false;
        }

        data_buf_.insert(data_buf_.end(), buffer.begin(), buffer.begin() + n);

        // Process complete packages
        while (data_buf_.size() >= 4) {
            int pkg_len = (data_buf_[1] << 16) | (data_buf_[2] << 8) | data_buf_[3];
            size_t total_len = 4 + pkg_len;

            if (data_buf_.size() < total_len) break;

            std::vector<uint8_t> pkg_data(data_buf_.begin(), data_buf_.begin() + total_len);
            auto pkg = protocol::Package::decode(pkg_data);
            if (pkg) {
                process_package(*pkg);
            }
            data_buf_.erase(data_buf_.begin(), data_buf_.begin() + total_len);
        }
    }

    return running_;
}

void Session::run() {
    // Legacy method, kept for compatibility but not used with epoll
    handle_read();
    close();
}

void Session::process_package(const protocol::Package& pkg) {
    switch (pkg.type) {
        case protocol::PackageType::Handshake:
            handle_handshake(pkg.body);
            break;
        case protocol::PackageType::HandshakeAck:
            handle_handshake_ack();
            break;
        case protocol::PackageType::Heartbeat:
            handle_heartbeat();
            break;
        case protocol::PackageType::Data:
            handle_data(pkg.body);
            break;
        case protocol::PackageType::Kick:
            close();
            break;
    }
}

void Session::handle_handshake(const std::vector<uint8_t>& /*body*/) {
    std::string response = R"({"code":200,"sys":{"heartbeat":10,"dict":{},"protos":{"client":{},"server":{}}},"user":{}})";
    std::vector<uint8_t> response_body(response.begin(), response.end());
    auto response_pkg = protocol::Package::encode(protocol::PackageType::Handshake, response_body);
    send(response_pkg);

    std::lock_guard lock(mutex_);
    state_ = ConnectionState::WaitAck;
}

void Session::handle_handshake_ack() {
    {
        std::lock_guard lock(mutex_);
        state_ = ConnectionState::Working;
        last_heartbeat_ = std::chrono::steady_clock::now();
    }

    // Start heartbeat coroutine
    auto task = heartbeat_coroutine();
    heartbeat_timer_id_ = scheduler_.add_timer_task(std::chrono::milliseconds(0), std::move(task));
}

void Session::handle_heartbeat() {
    {
        std::lock_guard lock(mutex_);
        last_heartbeat_ = std::chrono::steady_clock::now();
    }

    auto heartbeat_pkg = protocol::Package::encode(protocol::PackageType::Heartbeat, {});
    send(heartbeat_pkg);
}

void Session::handle_data(const std::vector<uint8_t>& body) {
    {
        std::lock_guard lock(mutex_);
        last_heartbeat_ = std::chrono::steady_clock::now();
    }

    auto msg = protocol::Message::decode(body);
    if (!msg) {
        std::cout << "[session] Failed to decode message" << std::endl;
        return;
    }

    std::string msg_body(msg->body.begin(), msg->body.end());

    if (msg->type == protocol::MessageType::Request) {
        handle_request(msg->id, msg->route, msg_body);
    } else if (msg->type == protocol::MessageType::Notify) {
        std::cout << "[session] Notify received: route=" << msg->route << ", body=" << msg_body << std::endl;
    }
}

void Session::handle_request(int id, const std::string& route, const std::string& body) {
    std::string response_body;

    {
        std::lock_guard lock(handlers_mutex_);
        auto it = handlers_.find(route);
        if (it != handlers_.end()) {
            json body_json;
            try {
                body_json = json::parse(body);
            } catch (const json::parse_error& e) {
                std::cout << "[session] Failed to parse JSON body: " << e.what() << std::endl;
                response_body = R"({"code":400,"msg":"Invalid JSON"})";
                std::vector<uint8_t> response_bytes(response_body.begin(), response_body.end());
                auto response_msg = protocol::Message::encode(id, protocol::MessageType::Response, false, "", response_bytes);
                auto response_pkg = protocol::Package::encode(protocol::PackageType::Data, response_msg);
                send(response_pkg);
                return;
            }
            response_body = it->second(*this, body_json);
        } else {
            std::cout << "[session] Unknown route: " << route << std::endl;
            response_body = R"({"code":404,"msg":"Route not found: )" + route + R"("})";
        }
    }

    std::vector<uint8_t> response_bytes(response_body.begin(), response_body.end());
    auto response_msg = protocol::Message::encode(id, protocol::MessageType::Response, false, "", response_bytes);
    auto response_pkg = protocol::Package::encode(protocol::PackageType::Data, response_msg);
    send(response_pkg);
}

Task Session::heartbeat_coroutine() {
    while (running_) {
        co_await std::chrono::milliseconds(std::chrono::duration_cast<std::chrono::milliseconds>(heartbeat_interval_).count());

        ConnectionState state;
        std::chrono::steady_clock::time_point last_hb;

        {
            std::lock_guard lock(mutex_);
            state = state_;
            last_hb = last_heartbeat_;
        }

        if (state != ConnectionState::Working) {
            co_return;
        }

        auto now = std::chrono::steady_clock::now();
        if (now - last_hb > heartbeat_timeout_) {
            std::cout << "[session] Heartbeat timeout" << std::endl;
            close();
            co_return;
        }

        auto heartbeat_pkg = protocol::Package::encode(protocol::PackageType::Heartbeat, {});
        send(heartbeat_pkg);
    }
}

void Session::send(const std::vector<uint8_t>& data) {
    ssize_t sent = 0;
    while (sent < static_cast<ssize_t>(data.size())) {
        ssize_t n = ::send(socket_fd_, data.data() + sent, data.size() - sent, 0);
        if (n < 0) {
            if (errno == EAGAIN || errno == EWOULDBLOCK) {
                // Socket buffer is full, yield and retry later
                // In coroutine context, this would yield, but here we just return
                // The data will be sent partially, caller should handle retry if needed
                return;
            }
            std::cout << "[session] Send error: " << strerror(errno) << std::endl;
            close();
            return;
        }
        sent += n;
    }
}

void Session::close() {
    bool expected = true;
    if (!running_.compare_exchange_strong(expected, false)) return;

    {
        std::lock_guard lock(mutex_);
        if (state_ == ConnectionState::Closed) return;
        state_ = ConnectionState::Closed;
    }

    // Remove heartbeat coroutine timer
    if (heartbeat_timer_id_ >= 0) {
        scheduler_.remove_timer(heartbeat_timer_id_);
        heartbeat_timer_id_ = -1;
    }

    ::close(socket_fd_);
    std::cout << "[session] Connection closed" << std::endl;
}

} // namespace server


