#include "poller.hpp"

#include <cerrno>
#include <unistd.h>

Poller::Poller() {
#if defined(__APPLE__)
    poller_fd_ = kqueue();
    if (poller_fd_ >= 0) {
        kev_buf_.resize(kMaxEvents);
    }
#else
    poller_fd_ = epoll_create1(0);
    if (poller_fd_ >= 0) {
        epoll_buf_.resize(kMaxEvents);
    }
#endif
}

Poller::~Poller() {
    if (poller_fd_ >= 0) {
        ::close(poller_fd_);
    }
}

bool Poller::is_valid() const {
    return poller_fd_ >= 0;
}

bool Poller::add_fd(int fd) {
    if (!is_valid()) return false;
#if defined(__APPLE__)
    struct kevent kev;
    EV_SET(&kev, fd, EVFILT_READ, EV_ADD | EV_ENABLE | EV_CLEAR, 0, 0, nullptr);
    return kevent(poller_fd_, &kev, 1, nullptr, 0, nullptr) == 0;
#else
    epoll_event ev{};
    ev.events = EPOLLIN | EPOLLET;
    ev.data.fd = fd;
    return epoll_ctl(poller_fd_, EPOLL_CTL_ADD, fd, &ev) == 0;
#endif
}

void Poller::remove_fd(int fd) {
    if (!is_valid()) return;
#if defined(__APPLE__)
    struct kevent kev;
    EV_SET(&kev, fd, EVFILT_READ, EV_DELETE, 0, 0, nullptr);
    kevent(poller_fd_, &kev, 1, nullptr, 0, nullptr);
#else
    epoll_ctl(poller_fd_, EPOLL_CTL_DEL, fd, nullptr);
#endif
}

int Poller::wait(std::vector<Event>& out_events, int timeout_ms) {
    if (!is_valid()) return -1;
    out_events.clear();

#if defined(__APPLE__)
    timespec ts{};
    timespec* tsp = nullptr;
    if (timeout_ms >= 0) {
        ts.tv_sec = timeout_ms / 1000;
        ts.tv_nsec = (timeout_ms % 1000) * 1'000'000;
        tsp = &ts;
    }
    int nfds = kevent(poller_fd_, nullptr, 0, kev_buf_.data(), static_cast<int>(kev_buf_.size()), tsp);
    if (nfds <= 0) {
        return nfds;
    }
    out_events.reserve(static_cast<size_t>(nfds));
    for (int i = 0; i < nfds; ++i) {
        Event ev;
        ev.fd = static_cast<int>(kev_buf_[i].ident);
        ev.readable = kev_buf_[i].filter == EVFILT_READ;
        ev.error = (kev_buf_[i].flags & (EV_EOF | EV_ERROR)) != 0;
        out_events.push_back(ev);
    }
    return nfds;
#else
    int nfds = epoll_wait(poller_fd_, epoll_buf_.data(), static_cast<int>(epoll_buf_.size()), timeout_ms);
    if (nfds <= 0) {
        return nfds;
    }
    out_events.reserve(static_cast<size_t>(nfds));
    for (int i = 0; i < nfds; ++i) {
        Event ev;
        ev.fd = epoll_buf_[i].data.fd;
        ev.readable = (epoll_buf_[i].events & EPOLLIN) != 0;
        ev.error = (epoll_buf_[i].events & (EPOLLERR | EPOLLHUP)) != 0;
        out_events.push_back(ev);
    }
    return nfds;
#endif
}

