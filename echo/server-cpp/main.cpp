#include <iostream>
#include <csignal>
#include <cstring>
#include <atomic>
#include <map>
#include <memory>
#include <fcntl.h>
#include <errno.h>
#include <vector>

#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>

#include "session.hpp"
#include "coroutine.hpp"
#include "json.hpp"
#include "poller.hpp"

using json = nlohmann::json;

constexpr int PORT = 3010;
std::atomic<bool> running{true};
int server_fd = -1;
std::map<int, std::shared_ptr<server::Session>> sessions;
server::Scheduler scheduler;

void signal_handler(int) {
    std::cout << "\n[main] Shutting down server..." << std::endl;
    running = false;
    if (server_fd >= 0) {
        shutdown(server_fd, SHUT_RDWR);
        ::close(server_fd);
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

    // Create platform-specific poller (epoll on Linux, kqueue on macOS)
    Poller poller;
    if (!poller.is_valid()) {
        std::cerr << "[main] Failed to create poller\n";
        return 1;
    }
    if (!poller.add_fd(server_fd)) {
        std::cerr << "[main] Failed to add server socket to poller\n";
        return 1;
    }
    std::vector<Poller::Event> events;
    events.reserve(64);

    std::cout << "[main] Server listening on port " << PORT << std::endl;

    while (running) {
        // Calculate timeout based on coroutine scheduler
        auto timeout_ms = scheduler.next_timeout();
        int timeout = timeout_ms.count() > 1000 ? 1000 : static_cast<int>(timeout_ms.count());
        
        int nfds = poller.wait(events, timeout);
        if (nfds < 0) {
            if (errno == EINTR) continue;
            std::cerr << "[main] poller wait error\n";
            break;
        }
        if (nfds == 0) {
            scheduler.tick();
            continue;
        }

        for (const auto& ev : events) {
            if (ev.fd == server_fd && ev.readable) {
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

                    // Add client socket to poller
                    if (!poller.add_fd(client_fd)) {
                        std::cerr << "[main] Failed to add client socket to poller\n";
                        ::close(client_fd);
                        continue;
                    }

                    // Create session
                    auto session = std::make_shared<server::Session>(client_fd, scheduler);
                    sessions[client_fd] = session;
                    session->start();
                }
            } else {
                // Client socket event
                int client_fd = ev.fd;
                auto it = sessions.find(client_fd);
                if (it == sessions.end()) {
                    continue;
                }

                auto session = it->second;

                if (ev.error) {
                    // Connection error or hangup
                    session->close();
                    poller.remove_fd(client_fd);
                    sessions.erase(it);
                    continue;
                }

                if (ev.readable) {
                    // Data available to read
                    if (!session->handle_read()) {
                        // Connection closed
                        session->close();
                        poller.remove_fd(client_fd);
                        sessions.erase(it);
                    }
                }
            }
        }

        // Process coroutine scheduler
        scheduler.tick();
    }

    // Cleanup
    for (auto& [fd, session] : sessions) {
        session->close();
        ::close(fd);
    }
    sessions.clear();

    if (server_fd >= 0) ::close(server_fd);

    return 0;
}

