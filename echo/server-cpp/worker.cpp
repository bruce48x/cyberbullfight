// WorkerThread implementation.
#include "worker.hpp"

#include <cerrno>
#include <cstring>
#include <fcntl.h>
#include <unistd.h>
#include <iostream>
#include <utility>
#include <vector>

namespace {
int set_nonblocking(int fd) {
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0) return -1;
    return fcntl(fd, F_SETFL, flags | O_NONBLOCK);
}
} // namespace

WorkerThread::WorkerThread(int worker_id, std::atomic<bool>& running_flag)
    : worker_id_(worker_id), running_(running_flag) {}

WorkerThread::~WorkerThread() {
    stop();
    join();
}

bool WorkerThread::start() {
    if (!poller_.is_valid()) {
        std::cerr << "[worker-" << worker_id_ << "] Failed to create poller\n";
        return false;
    }
    thread_ = std::thread(&WorkerThread::thread_func, this);
    return true;
}

void WorkerThread::stop() {
    should_stop_ = true;
}

void WorkerThread::join() {
    if (thread_.joinable()) {
        thread_.join();
    }
}

void WorkerThread::enqueue_connection(int client_fd) {
    std::lock_guard<std::mutex> lock(mutex_);
    pending_connections_.push(client_fd);
}

void WorkerThread::process_pending_connections() {
    std::unique_lock<std::mutex> lock(mutex_);
    while (!pending_connections_.empty()) {
        int client_fd = pending_connections_.front();
        pending_connections_.pop();
        lock.unlock();

        // Set client socket to non-blocking
        if (set_nonblocking(client_fd) < 0) {
            std::cerr << "[worker-" << worker_id_ << "] Failed to set client socket non-blocking\n";
            ::close(client_fd);
            lock.lock();
            continue;
        }

        // Add client socket to poller
        if (!poller_.add_fd(client_fd)) {
            std::cerr << "[worker-" << worker_id_ << "] Failed to add client socket to poller\n";
            ::close(client_fd);
            lock.lock();
            continue;
        }

        // Create session
        {
            std::lock_guard<std::mutex> session_lock(mutex_);
            auto session = std::make_shared<server::Session>(client_fd, scheduler_);
            sessions_[client_fd] = session;
            session->start();
        }

        lock.lock();
    }
}

void WorkerThread::thread_func() {
    std::vector<Poller::Event> events;
    events.reserve(64);

    std::cout << "[worker-" << worker_id_ << "] Started" << std::endl;

    while (running_ || !should_stop_) {
        // Handle newly assigned connections
        process_pending_connections();

        // Calculate timeout based on coroutine scheduler
        auto timeout_ms = scheduler_.next_timeout();
        int timeout = timeout_ms.count() > 1000 ? 1000 : static_cast<int>(timeout_ms.count());
        {
            std::lock_guard<std::mutex> lock(mutex_);
            if (!pending_connections_.empty()) {
                timeout = 0; // Don't block if new connections are waiting
            }
        }

        int nfds = poller_.wait(events, timeout);
        if (nfds < 0) {
            if (errno == EINTR) continue;
            if ((errno == EBADF && should_stop_) || (should_stop_ && !running_)) break;
            std::cerr << "[worker-" << worker_id_ << "] poller wait error: " << strerror(errno) << "\n";
            continue;
        }
        if (nfds == 0) {
            // Timeout - process scheduler
            scheduler_.tick();
            continue;
        }

        for (const auto& ev : events) {
            std::lock_guard<std::mutex> lock(mutex_);
            auto it = sessions_.find(ev.fd);
            if (it == sessions_.end()) {
                continue;
            }

            auto session = it->second;

            if (ev.error) {
                session->close();
                poller_.remove_fd(ev.fd);
                sessions_.erase(it);
                continue;
            }

            if (ev.readable) {
                if (!session->handle_read()) {
                    session->close();
                    poller_.remove_fd(ev.fd);
                    sessions_.erase(it);
                }
            }
        }

        // Process coroutine scheduler
        scheduler_.tick();
    }

    // Cleanup
    {
        std::lock_guard<std::mutex> lock(mutex_);
        for (auto& [fd, session] : sessions_) {
            session->close();
            ::close(fd);
        }
        sessions_.clear();
    }

    std::cout << "[worker-" << worker_id_ << "] Stopped" << std::endl;
}
