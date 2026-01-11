#pragma once

#include <vector>

#if defined(__APPLE__)
#include <sys/event.h>
#include <sys/time.h>
#else
#include <sys/epoll.h>
#endif

// Simple cross-platform polling abstraction.
// Uses kqueue on macOS and epoll on Linux.
class Poller {
public:
    struct Event {
        int fd;
        bool readable;
        bool error;
    };

    Poller();
    ~Poller();

    bool is_valid() const;
    bool add_fd(int fd);
    void remove_fd(int fd);

    // Wait for events up to timeout_ms (milliseconds).
    // Returns number of events, 0 on timeout, or -1 on error.
    int wait(std::vector<Event>& out_events, int timeout_ms);

private:
    int poller_fd_{-1};
    static constexpr int kMaxEvents = 64;

#if defined(__APPLE__)
    // Storage for kqueue events to avoid reallocating each wait.
    std::vector<struct kevent> kev_buf_;
#else
    // Storage for epoll events to avoid reallocating each wait.
    std::vector<struct epoll_event> epoll_buf_;
#endif
};

