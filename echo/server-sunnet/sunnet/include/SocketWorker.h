#pragma once

#include <sys/epoll.h>
#include <memory>
#include "Conn.h"

using namespace std;

class SocketWorker
{
public:
    void Init();
    void operator()();
    void AddEvent(int fd);
    void RemoveEvent(int fd);
    void ModifyEvent(int fd, bool epollOut);

private:
    int epollFd;
    void OnEvent(epoll_event ev);
    void OnAccept(shared_ptr<Conn> conn);
    void OnRW(shared_ptr<Conn> conn, bool r, bool w);
};