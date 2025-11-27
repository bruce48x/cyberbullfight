#include "session.hpp"

#include <iostream>
#include <cstring>
#include <unistd.h>
#include <sys/socket.h>

namespace server {

std::map<std::string, RouteHandler> Session::handlers_;
std::mutex Session::handlers_mutex_;

void Session::register_handler(const std::string& route, RouteHandler handler) {
    std::lock_guard lock(handlers_mutex_);
    handlers_[route] = std::move(handler);
}

Session::Session(int socket_fd) : socket_fd_(socket_fd) {}

Session::~Session() {
    close();
    if (read_thread_.joinable()) read_thread_.join();
    if (heartbeat_thread_.joinable()) heartbeat_thread_.join();
}

void Session::start() {
    read_thread_ = std::thread([self = shared_from_this()]() { self->run(); });
}

void Session::run() {
    std::vector<uint8_t> buffer(4096);
    std::vector<uint8_t> data_buf;

    while (running_) {
        ssize_t n = recv(socket_fd_, buffer.data(), buffer.size(), 0);
        if (n <= 0) {
            std::cout << "[session] Connection closed by client\n";
            break;
        }

        data_buf.insert(data_buf.end(), buffer.begin(), buffer.begin() + n);

        // Process complete packages
        while (data_buf.size() >= 4) {
            int pkg_len = (data_buf[1] << 16) | (data_buf[2] << 8) | data_buf[3];
            size_t total_len = 4 + pkg_len;

            if (data_buf.size() < total_len) break;

            std::vector<uint8_t> pkg_data(data_buf.begin(), data_buf.begin() + total_len);
            auto pkg = protocol::Package::decode(pkg_data);
            if (pkg) {
                process_package(*pkg);
            }
            data_buf.erase(data_buf.begin(), data_buf.begin() + total_len);
        }
    }

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

    heartbeat_thread_ = std::thread([self = shared_from_this()]() { self->heartbeat_loop(); });
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
        std::cout << "[session] Failed to decode message\n";
        return;
    }

    std::string msg_body(msg->body.begin(), msg->body.end());

    if (msg->type == protocol::MessageType::Request) {
        handle_request(msg->id, msg->route, msg_body);
    } else if (msg->type == protocol::MessageType::Notify) {
        std::cout << "[session] Notify received: route=" << msg->route << ", body=" << msg_body << "\n";
    }
}

void Session::handle_request(int id, const std::string& route, const std::string& body) {
    std::string response_body;

    {
        std::lock_guard lock(handlers_mutex_);
        auto it = handlers_.find(route);
        if (it != handlers_.end()) {
            response_body = it->second(route, body);
        } else {
            std::cout << "[session] Unknown route: " << route << "\n";
            response_body = R"({"code":404,"msg":"Route not found: )" + route + R"("})";
        }
    }

    std::vector<uint8_t> response_bytes(response_body.begin(), response_body.end());
    auto response_msg = protocol::Message::encode(id, protocol::MessageType::Response, false, "", response_bytes);
    auto response_pkg = protocol::Package::encode(protocol::PackageType::Data, response_msg);
    send(response_pkg);
}

void Session::heartbeat_loop() {
    while (running_) {
        std::this_thread::sleep_for(heartbeat_interval_);

        ConnectionState state;
        std::chrono::steady_clock::time_point last_hb;

        {
            std::lock_guard lock(mutex_);
            state = state_;
            last_hb = last_heartbeat_;
        }

        if (state != ConnectionState::Working) return;

        auto now = std::chrono::steady_clock::now();
        if (now - last_hb > heartbeat_timeout_) {
            std::cout << "[session] Heartbeat timeout\n";
            close();
            return;
        }

        auto heartbeat_pkg = protocol::Package::encode(protocol::PackageType::Heartbeat, {});
        send(heartbeat_pkg);
    }
}

void Session::send(const std::vector<uint8_t>& data) {
    ::send(socket_fd_, data.data(), data.size(), 0);
}

void Session::close() {
    bool expected = true;
    if (!running_.compare_exchange_strong(expected, false)) return;

    {
        std::lock_guard lock(mutex_);
        if (state_ == ConnectionState::Closed) return;
        state_ = ConnectionState::Closed;
    }

    ::close(socket_fd_);
    std::cout << "[session] Connection closed\n";
}

} // namespace server

