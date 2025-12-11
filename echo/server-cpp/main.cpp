#include <iostream>
#include <csignal>
#include <cstring>
#include <atomic>
#include <map>
#include <memory>
#include <fcntl.h>
#include <errno.h>

#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>
#include <sys/epoll.h>

#include "session.hpp"
#include "json.hpp"

using json = nlohmann::json;

constexpr int PORT = 3010;
constexpr int MAX_EVENTS = 64;

std::atomic<bool> running{true};
int server_fd = -1;
int epoll_fd = -1;
std::map<int, std::shared_ptr<server::Session>> sessions;

void signal_handler(int) {
    std::cout << "\n[main] Shutting down server..." << std::endl;
    running = false;
    if (server_fd >= 0) {
        shutdown(server_fd, SHUT_RDWR);
        ::close(server_fd);
    }
    if (epoll_fd >= 0) {
        ::close(epoll_fd);
    }
}

int set_nonblocking(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0) return -1;
    return fcntl(fd, F_SETFL, flags | O_NONBLOCK);
}

int main() {
    // Disable buffering for stdout to ensure logs appear immediately in containers
    std::cout.setf(std::ios::unitbuf);
    std::cerr.setf(std::ios::unitbuf);
    
    // Register signal handlers
    std::signal(SIGINT, signal_handler);
    std::signal(SIGTERM, signal_handler);

    // Register handlers
    server::Session::register_handler("connector.entryHandler.hello",
        [](server::Session& s, json body) {
            s.ReqId++;
            body["serverReqId"] = s.ReqId;
            json resp;
            resp["code"] = 0;
            resp["msg"] = body;
            return resp.dump();
        });

    // Create socket
    server_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (server_fd < 0) {
        std::cerr << "[main] Failed to create socket\n";
        return 1;
    }

    int opt = 1;
    setsockopt(server_fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

    // Set server socket to non-blocking
    if (set_nonblocking(server_fd) < 0) {
        std::cerr << "[main] Failed to set server socket non-blocking\n";
        return 1;
    }

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port = htons(PORT);

    if (bind(server_fd, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0) {
        std::cerr << "[main] Failed to bind\n";
        return 1;
    }

    if (listen(server_fd, 10) < 0) {
        std::cerr << "[main] Failed to listen\n";
        return 1;
    }

    // Create epoll instance
    epoll_fd = epoll_create1(0);
    if (epoll_fd < 0) {
        std::cerr << "[main] Failed to create epoll\n";
        return 1;
    }

    // Add server socket to epoll
    epoll_event ev{};
    ev.events = EPOLLIN;
    ev.data.fd = server_fd;
    if (epoll_ctl(epoll_fd, EPOLL_CTL_ADD, server_fd, &ev) < 0) {
        std::cerr << "[main] Failed to add server socket to epoll\n";
        return 1;
    }

    std::cout << "[main] Server listening on port " << PORT << std::endl;

    epoll_event events[MAX_EVENTS];

    while (running) {
        int nfds = epoll_wait(epoll_fd, events, MAX_EVENTS, 1000); // 1 second timeout
        if (nfds < 0) {
            if (errno == EINTR) continue;
            std::cerr << "[main] epoll_wait error\n";
            break;
        }

        for (int i = 0; i < nfds; ++i) {
            if (events[i].data.fd == server_fd) {
                // New connection
                while (true) {
                    sockaddr_in client_addr{};
                    socklen_t client_len = sizeof(client_addr);
                    int client_fd = accept(server_fd, reinterpret_cast<sockaddr*>(&client_addr), &client_len);

                    if (client_fd < 0) {
                        if (errno == EAGAIN || errno == EWOULDBLOCK) {
                            // No more connections to accept
                            break;
                        }
                        std::cerr << "[main] Accept error\n";
                        break;
                    }

                    // Set client socket to non-blocking
                    if (set_nonblocking(client_fd) < 0) {
                        std::cerr << "[main] Failed to set client socket non-blocking\n";
                        ::close(client_fd);
                        continue;
                    }

                    char ip[INET_ADDRSTRLEN];
                    inet_ntop(AF_INET, &client_addr.sin_addr, ip, sizeof(ip));
                    std::cout << "[main] Client connected: " << ip << ":" << ntohs(client_addr.sin_port) << std::endl;

                    // Add client socket to epoll
                    epoll_event client_ev{};
                    client_ev.events = EPOLLIN | EPOLLET; // Edge-triggered mode
                    client_ev.data.fd = client_fd;
                    if (epoll_ctl(epoll_fd, EPOLL_CTL_ADD, client_fd, &client_ev) < 0) {
                        std::cerr << "[main] Failed to add client socket to epoll\n";
                        ::close(client_fd);
                        continue;
                    }

                    // Create session
                    auto session = std::make_shared<server::Session>(client_fd);
                    sessions[client_fd] = session;
                    session->start();
                }
            } else {
                // Client socket event
                int client_fd = events[i].data.fd;
                auto it = sessions.find(client_fd);
                if (it == sessions.end()) {
                    continue;
                }

                auto session = it->second;

                if (events[i].events & (EPOLLERR | EPOLLHUP)) {
                    // Connection error or hangup
                    session->close();
                    epoll_ctl(epoll_fd, EPOLL_CTL_DEL, client_fd, nullptr);
                    sessions.erase(it);
                    continue;
                }

                if (events[i].events & EPOLLIN) {
                    // Data available to read
                    if (!session->handle_read()) {
                        // Connection closed
                        session->close();
                        epoll_ctl(epoll_fd, EPOLL_CTL_DEL, client_fd, nullptr);
                        sessions.erase(it);
                    }
                }
            }
        }
    }

    // Cleanup
    for (auto& [fd, session] : sessions) {
        session->close();
        ::close(fd);
    }
    sessions.clear();

    if (epoll_fd >= 0) ::close(epoll_fd);
    if (server_fd >= 0) ::close(server_fd);

    return 0;
}

