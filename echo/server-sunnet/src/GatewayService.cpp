#include "GatewayService.h"
#include "SessionManager.h"
#include "../sunnet/include/Sunnet.h"
#include <iostream>
#include <sys/socket.h>
#include <unistd.h>
#include <errno.h>
#include <cstring>
#include <chrono>
#include <thread>

using namespace std;

unordered_map<string, RouteHandler> GatewayService::handlers_;
mutex GatewayService::handlers_mutex_;

// 注册 GatewayService 到服务工厂（需要在 main 函数中调用）
void GatewayService::register_service() {
    Sunnet::RegisterService("gateway", []() -> shared_ptr<Service> {
        return make_shared<GatewayService>();
    });
}

void GatewayService::register_handler(const string& route, RouteHandler handler) {
    lock_guard<mutex> lock(handlers_mutex_);
    handlers_[route] = move(handler);
}

GatewayService::GatewayService() {
    // Start heartbeat thread
    heartbeat_thread_ = thread([this]() {
        while (heartbeat_running_) {
            this_thread::sleep_for(chrono::seconds(1));
            check_heartbeat_timeout();
        }
    });
}

GatewayService::~GatewayService() {
    heartbeat_running_ = false;
    if (heartbeat_thread_.joinable()) {
        heartbeat_thread_.join();
    }
}

void GatewayService::OnInit() {
    cout << "[GatewayService] OnInit id=" << id << endl;
}

void GatewayService::OnMsg(shared_ptr<BaseMsg> msg) {
    if (msg->type == BaseMsg::TYPE::SOCKET_ACCEPT) {
        auto m = dynamic_pointer_cast<SocketAcceptMsg>(msg);
        OnAcceptMsg(m);
    } else if (msg->type == BaseMsg::TYPE::SOCKET_RW) {
        auto m = dynamic_pointer_cast<SocketRWMsg>(msg);
        OnRWMsg(m);
    }
}

void GatewayService::OnExit() {
    cout << "[GatewayService] OnExit id=" << id << endl;
    lock_guard<mutex> lock(sessions_mutex_);
    for (auto& pair : sessions_) {
        Sunnet::inst->CloseConn(pair.first);
    }
    sessions_.clear();
}

void GatewayService::OnAcceptMsg(shared_ptr<SocketAcceptMsg> msg) {
    int clientFd = msg->clientFd;
    cout << "[GatewayService] OnAcceptMsg clientFd=" << clientFd << endl;

    auto session = make_shared<Session>();
    session->fd = clientFd;
    session->state = ConnectionState::Inited;
    session->last_heartbeat = chrono::steady_clock::now();

    lock_guard<mutex> lock(sessions_mutex_);
    sessions_[clientFd] = session;
}

void GatewayService::OnRWMsg(shared_ptr<SocketRWMsg> msg) {
    int fd = msg->fd;
    if (msg->isRead) {
        const int BUFFSIZE = 4096;
        char buff[BUFFSIZE];
        int len = 0;
        do {
            len = read(fd, &buff, BUFFSIZE);
            if (len > 0) {
                OnSocketData(fd, buff, len);
            }
        } while (len == BUFFSIZE);

        if (len <= 0 && errno != EAGAIN) {
            lock_guard<mutex> lock(sessions_mutex_);
            if (sessions_.find(fd) != sessions_.end()) {
                OnSocketClose(fd);
                Sunnet::inst->CloseConn(fd);
            }
        }
    }
    if (msg->isWrite) {
        lock_guard<mutex> lock(sessions_mutex_);
        if (sessions_.find(fd) != sessions_.end()) {
            OnSocketWritable(fd);
        }
    }
}

void GatewayService::OnSocketData(int fd, const char *buff, int len) {
    // Get session and copy data buffer
    shared_ptr<Session> session;
    {
        lock_guard<mutex> lock(sessions_mutex_);
        auto it = sessions_.find(fd);
        if (it == sessions_.end()) {
            cout << "[GatewayService] Session not found for fd=" << fd << endl;
            return;
        }
        session = it->second;
        session->data_buf.insert(session->data_buf.end(), buff, buff + len);
    }

    // Process complete packages (without holding lock)
    while (true) {
        vector<uint8_t> pkg_data;
        bool has_package = false;
        
        {
            lock_guard<mutex> lock(sessions_mutex_);
            auto it = sessions_.find(fd);
            if (it == sessions_.end()) {
                return;
            }
            session = it->second;
            
            if (session->data_buf.size() >= 4) {
                int pkg_type = session->data_buf[0];
                int pkg_len = (session->data_buf[1] << 16) | (session->data_buf[2] << 8) | session->data_buf[3];
                size_t total_len = 4 + pkg_len;

                if (session->data_buf.size() >= total_len) {
                    pkg_data.assign(session->data_buf.begin(), session->data_buf.begin() + total_len);
                    session->data_buf.erase(session->data_buf.begin(), session->data_buf.begin() + total_len);
                    has_package = true;
                }
            }
        }

        if (!has_package) {
            break;
        }

        auto pkg = protocol::Package::decode(pkg_data);
        if (pkg) {
            process_package(fd, *pkg);
        } else {
            cout << "[GatewayService] Failed to decode package" << endl;
        }
    }
}

void GatewayService::OnSocketWritable(int fd) {
    // Handle write buffer if needed
}

void GatewayService::OnSocketClose(int fd) {
    cout << "[GatewayService] OnSocketClose fd=" << fd << endl;
    lock_guard<mutex> lock(sessions_mutex_);
    bool had_session = (sessions_.erase(fd) > 0);
    
    // Notify session manager of connection close (only if session existed)
    if (had_session) {
        SessionManager::getInstance().onConnectionClose();
    }
}

void GatewayService::process_package(int fd, const protocol::Package& pkg) {
    switch (pkg.type) {
        case protocol::PackageType::Handshake:
            handle_handshake(fd, pkg.body);
            break;
        case protocol::PackageType::HandshakeAck:
            handle_handshake_ack(fd);
            break;
        case protocol::PackageType::Heartbeat:
            handle_heartbeat(fd);
            break;
        case protocol::PackageType::Data:
            handle_data(fd, pkg.body);
            break;
        case protocol::PackageType::Kick:
            OnSocketClose(fd);
            Sunnet::inst->CloseConn(fd);
            break;
        default:
            cout << "[GatewayService] Unknown package type: " << (int)static_cast<uint8_t>(pkg.type) << endl;
            break;
    }
}

void GatewayService::handle_handshake(int fd, const vector<uint8_t>& /*body*/) {
    cout << "[GatewayService] handle_handshake fd=" << fd << endl;
    string response = R"({"code":200,"sys":{"heartbeat":10,"dict":{},"protos":{"client":{},"server":{}}},"user":{}})";
    vector<uint8_t> response_body(response.begin(), response.end());
    auto response_pkg = protocol::Package::encode(protocol::PackageType::Handshake, response_body);
    cout << "[GatewayService] Sending handshake response, size=" << response_pkg.size() << endl;
    send(fd, response_pkg);

    lock_guard<mutex> lock(sessions_mutex_);
    auto it = sessions_.find(fd);
    if (it != sessions_.end()) {
        it->second->state = ConnectionState::WaitAck;
        it->second->heartbeat_interval = chrono::seconds(10);
        it->second->heartbeat_timeout = chrono::seconds(20);
        cout << "[GatewayService] Handshake response sent, state=WaitAck" << endl;
    }
}

void GatewayService::handle_handshake_ack(int fd) {
    lock_guard<mutex> lock(sessions_mutex_);
    auto it = sessions_.find(fd);
    if (it != sessions_.end()) {
        it->second->state = ConnectionState::Working;
        it->second->last_heartbeat = chrono::steady_clock::now();
        
        // Notify session manager of successful handshake
        SessionManager::getInstance().onHandshakeSuccess();
    }
}

void GatewayService::handle_heartbeat(int fd) {
    lock_guard<mutex> lock(sessions_mutex_);
    auto it = sessions_.find(fd);
    if (it != sessions_.end()) {
        it->second->last_heartbeat = chrono::steady_clock::now();
    }

    auto heartbeat_pkg = protocol::Package::encode(protocol::PackageType::Heartbeat, {});
    send(fd, heartbeat_pkg);
}

void GatewayService::handle_data(int fd, const vector<uint8_t>& body) {
    {
        lock_guard<mutex> lock(sessions_mutex_);
        auto it = sessions_.find(fd);
        if (it != sessions_.end()) {
            it->second->last_heartbeat = chrono::steady_clock::now();
        }
    }

    auto msg = protocol::Message::decode(body);
    if (!msg) {
        cout << "[GatewayService] Failed to decode message, body_size=" << body.size() << endl;
        return;
    }

    string msg_body(msg->body.begin(), msg->body.end());
    if (msg->type == protocol::MessageType::Request) {
        handle_request(fd, msg->id, msg->route, msg_body);
    } else if (msg->type == protocol::MessageType::Notify) {
        cout << "[GatewayService] Notify received: route=" << msg->route << ", body=" << msg_body << endl;
    } else {
        cout << "[GatewayService] Unknown message type: " << (int)static_cast<int>(msg->type) << endl;
    }
}

void GatewayService::handle_request(int fd, int id, const string& route, const string& body) {
    cout << "[GatewayService] handle_request fd=" << fd << ", id=" << id << ", route=" << route << ", body=" << body << endl;
    string response_body;

    lock_guard<mutex> lock(handlers_mutex_);
    auto it = handlers_.find(route);
    if (it != handlers_.end()) {
        lock_guard<mutex> session_lock(sessions_mutex_);
        auto session_it = sessions_.find(fd);
        if (session_it == sessions_.end()) {
            cout << "[GatewayService] Session not found for fd=" << fd << endl;
            return;
        }
        auto session = session_it->second;

        json body_json;
        try {
            if (!body.empty()) {
                body_json = json::parse(body);
            }
        } catch (const json::parse_error& e) {
            cout << "[GatewayService] Failed to parse JSON body: " << e.what() << endl;
            response_body = R"({"code":400,"msg":"Invalid JSON"})";
            vector<uint8_t> response_bytes(response_body.begin(), response_body.end());
            auto response_msg = protocol::Message::encode(id, protocol::MessageType::Response, false, "", response_bytes);
            auto response_pkg = protocol::Package::encode(protocol::PackageType::Data, response_msg);
            cout << "[GatewayService] Sending error response, pkg_size=" << response_pkg.size() << endl;
            send(fd, response_pkg);
            return;
        }

        response_body = it->second(*session, body_json);
    } else {
        cout << "[GatewayService] Unknown route: " << route << endl;
        response_body = R"({"code":404,"msg":"Route not found: )" + route + R"("})";
    }

    vector<uint8_t> response_bytes(response_body.begin(), response_body.end());
    auto response_msg = protocol::Message::encode(id, protocol::MessageType::Response, false, "", response_bytes);
    auto response_pkg = protocol::Package::encode(protocol::PackageType::Data, response_msg);
    send(fd, response_pkg);
}

void GatewayService::send(int fd, const vector<uint8_t>& data) {
    ssize_t sent = 0;
    while (sent < static_cast<ssize_t>(data.size())) {
        ssize_t n = ::send(fd, data.data() + sent, data.size() - sent, 0);
        if (n < 0) {
            if (errno == EAGAIN || errno == EWOULDBLOCK) {
                // Socket buffer is full, will retry later
                cout << "[GatewayService] Send would block, fd=" << fd << ", sent=" << sent << "/" << data.size() << endl;
                // TODO: Add to write buffer and enable EPOLLOUT
                return;
            }
            cout << "[GatewayService] Send error: " << strerror(errno) << ", fd=" << fd << endl;
            OnSocketClose(fd);
            Sunnet::inst->CloseConn(fd);
            return;
        }
        sent += n;
    }
}

void GatewayService::send_heartbeat(int fd) {
    auto heartbeat_pkg = protocol::Package::encode(protocol::PackageType::Heartbeat, {});
    send(fd, heartbeat_pkg);
}

void GatewayService::check_heartbeat_timeout() {
    auto now = chrono::steady_clock::now();
    vector<int> timeout_fds;

    {
        lock_guard<mutex> lock(sessions_mutex_);
        for (auto& pair : sessions_) {
            auto session = pair.second;
            if (session->state == ConnectionState::Working) {
                auto elapsed = chrono::duration_cast<chrono::seconds>(now - session->last_heartbeat);
                if (elapsed > session->heartbeat_timeout) {
                    timeout_fds.push_back(pair.first);
                } else if (elapsed >= session->heartbeat_interval) {
                    send_heartbeat(pair.first);
                }
            }
        }
    }

    for (int fd : timeout_fds) {
        cout << "[GatewayService] Heartbeat timeout fd=" << fd << endl;
        OnSocketClose(fd);
        Sunnet::inst->CloseConn(fd);
    }
}
