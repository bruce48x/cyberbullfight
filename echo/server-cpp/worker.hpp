// Worker thread abstraction for the echo server.
// Each worker owns its own Poller, Scheduler, and session map, and processes
// connections assigned by the main acceptor thread.
#pragma once

#include <atomic>
#include <map>
#include <memory>
#include <mutex>
#include <queue>
#include <thread>

#include "poller.hpp"
#include "session.hpp"

class WorkerThread {
public:
    WorkerThread(int worker_id, std::atomic<bool>& running_flag);
    ~WorkerThread();

    bool start();
    void stop();
    void join();

    // Enqueue a new accepted client socket to this worker.
    void enqueue_connection(int client_fd);

    int id() const { return worker_id_; }

private:
    void thread_func();
    void process_pending_connections();

    std::thread thread_;
    Poller poller_;
    server::Scheduler scheduler_;
    std::map<int, std::shared_ptr<server::Session>> sessions_;
    std::mutex mutex_;
    std::queue<int> pending_connections_;
    std::atomic<bool> should_stop_{false};
    int worker_id_;
    std::atomic<bool>& running_;
};
