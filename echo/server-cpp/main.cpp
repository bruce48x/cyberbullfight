#include <iostream>
#include <csignal>
#include <cstring>
#include <atomic>

#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>

#include "session.hpp"

constexpr int PORT = 3010;

std::atomic<bool> running{true};
int server_fd = -1;

void signal_handler(int) {
    std::cout << "\n[main] Shutting down server..." << std::endl;
    running = false;
    if (server_fd >= 0) {
        shutdown(server_fd, SHUT_RDWR);
        ::close(server_fd);
    }
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
        [](const std::string& /*route*/, const std::string& body) {
            return R"({"code":0,"msg":)" + body + "}";
        });

    // Create socket
    server_fd = socket(AF_INET, SOCK_STREAM, 0);
    if (server_fd < 0) {
        std::cerr << "[main] Failed to create socket\n";
        return 1;
    }

    int opt = 1;
    setsockopt(server_fd, SOL_SOCKET, SO_REUSEADDR, &opt, sizeof(opt));

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

    std::cout << "[main] Server listening on port " << PORT << std::endl;

    while (running) {
        sockaddr_in client_addr{};
        socklen_t client_len = sizeof(client_addr);
        int client_fd = accept(server_fd, reinterpret_cast<sockaddr*>(&client_addr), &client_len);

        if (client_fd < 0) {
            if (running) std::cerr << "[main] Accept error\n";
            continue;
        }

        char ip[INET_ADDRSTRLEN];
        inet_ntop(AF_INET, &client_addr.sin_addr, ip, sizeof(ip));
        std::cout << "[main] Client connected: " << ip << ":" << ntohs(client_addr.sin_port) << std::endl;

        auto session = std::make_shared<server::Session>(client_fd);
        session->start();
    }

    return 0;
}

