#include <iostream>
#include <csignal>
#include <cstring>
#include <atomic>
#include <memory>
#include <fcntl.h>
#include <errno.h>
#include <vector>
#include <thread>

#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <unistd.h>

#include "session.hpp"
#include "coroutine.hpp"
#include "json.hpp"
#include "poller.hpp"
#include "worker.hpp"

using json = nlohmann::json;

constexpr int PORT = 3010;
std::atomic<bool> running{true};
int server_fd = -1;
std::vector<std::unique_ptr<WorkerThread>> workers;
std::atomic<int> next_worker{0};

int set_nonblocking(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0) return -1;
    return fcntl(fd, F_SETFL, flags | O_NONBLOCK);
}

void signal_handler(int) {
    std::cout << "\n[main] Shutting down server..." << std::endl;
    running = false;
    if (server_fd >= 0) {
        shutdown(server_fd, SHUT_RDWR);
        ::close(server_fd);
    }
    // Signal all workers to stop
    for (auto& worker : workers) {
        worker->stop();
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
        [](server::Session& s, json body) {
            s.ReqId++;
            body["serverReqId"] = s.ReqId;
            json resp;
            resp["code"] = 0;
            resp["msg"] = body;
            return resp.dump();
        });

    // Detect CPU core count and create worker threads
    unsigned int num_workers = std::thread::hardware_concurrency();
    if (num_workers == 0) {
        num_workers = 4; // Fallback to 4 if detection fails
    }
    // Use all cores, or num_workers - 1 if you want to reserve one for main thread
    // Here we use all cores since main thread only does accept()
    std::cout << "[main] Detected " << num_workers << " CPU cores, creating " << num_workers << " worker threads" << std::endl;

    // Create worker threads
    workers.resize(num_workers);
    for (unsigned int i = 0; i < num_workers; ++i) {
        auto worker = std::make_unique<WorkerThread>(static_cast<int>(i), running);
        if (!worker->start()) {
            std::cerr << "[main] Failed to start worker-" << i << std::endl;
            return 1;
        }
        workers[i] = std::move(worker);
    }

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

    // Main thread only handles accepting new connections
    // Use a simple poller for the server socket
    Poller main_poller;
    if (!main_poller.is_valid()) {
        std::cerr << "[main] Failed to create poller\n";
        return 1;
    }
    if (!main_poller.add_fd(server_fd)) {
        std::cerr << "[main] Failed to add server socket to poller\n";
        return 1;
    }
    std::vector<Poller::Event> events;
    events.reserve(64);

    std::cout << "[main] Server listening on port " << PORT << std::endl;

    while (running) {
        int timeout = 1000; // 1 second timeout
        int nfds = main_poller.wait(events, timeout);
        if (nfds < 0) {
            if (errno == EINTR) continue;
            std::cerr << "[main] poller wait error\n";
            break;
        }
        if (nfds == 0) {
            continue; // Timeout, check running flag
        }

        for (const auto& ev : events) {
            if (ev.fd == server_fd && ev.readable) {
                // New connection - accept and distribute to worker threads
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

                    char ip[INET_ADDRSTRLEN];
                    inet_ntop(AF_INET, &client_addr.sin_addr, ip, sizeof(ip));
                    std::cout << "[main] Client connected: " << ip << ":" << ntohs(client_addr.sin_port) << std::endl;

                    // Distribute connection to a worker thread using round-robin
                    int worker_idx = next_worker.fetch_add(1) % num_workers;
                    auto& worker = workers[worker_idx];

                    worker->enqueue_connection(client_fd);
                }
            }
        }
    }

    // Wait for all worker threads to finish
    std::cout << "[main] Waiting for worker threads to finish..." << std::endl;
    for (auto& worker : workers) {
        worker->stop();
        worker->join();
    }

    if (server_fd >= 0) ::close(server_fd);

    std::cout << "[main] Server shutdown complete" << std::endl;
    return 0;
}

