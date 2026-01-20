#include "SocketWorker.h"
#include <iostream>
#include <unistd.h>
#include <cassert>
#include <cerrno>
#include <cstring>
#include "Sunnet.h"
#include <sys/socket.h>
#include <fcntl.h>

void SocketWorker::Init()
{
    cout << "SocketWorker Init" << endl;
    epollFd = epoll_create(1024);
    assert(epollFd > 0);
}

void SocketWorker::operator()()
{
    while (true)
    {
        // 阻塞等待
        const int EVENT_SIZE = 64;
        struct epoll_event events[EVENT_SIZE];
        int eventCount = epoll_wait(epollFd, events, EVENT_SIZE, -1);
        // 取得事件
        for (int i = 0; i < eventCount; i++)
        {
            epoll_event ev = events[i]; // 当前要处理的事件
            OnEvent(ev);
        }
    }
}

void SocketWorker::AddEvent(int fd)
{
    cout << "AddEvent fd " << fd << endl;
    // 添加到 epoll 对象
    struct epoll_event ev;
    ev.events = EPOLLIN | EPOLLET;
    ev.data.fd = fd;
    if (epoll_ctl(epollFd, EPOLL_CTL_ADD, fd, &ev) == -1)
    {
        cout << "AddEvent epoll_ctl fail:" << strerror(errno) << endl;
    }
}

void SocketWorker::RemoveEvent(int fd)
{
    cout << "RemoveEvent fd " << fd << endl;
    epoll_ctl(epollFd, EPOLL_CTL_DEL, fd, NULL);
}

void SocketWorker::ModifyEvent(int fd, bool epollOut)
{
    cout << "ModifyEvent fd " << fd << " " << epollOut << endl;
    struct epoll_event ev;
    ev.data.fd = fd;
    if (epollOut)
    {
        ev.events = EPOLLIN | EPOLLET | EPOLLOUT;
    }
    else
    {
        ev.events = EPOLLIN | EPOLLET;
    }
    epoll_ctl(epollFd, EPOLL_CTL_MOD, fd, &ev);
}

void SocketWorker::OnEvent(epoll_event ev)
{
    int fd = ev.data.fd;
    auto conn = Sunnet::inst->GetConn(fd);
    if (conn == NULL)
    {
        cout << "OnEvent error, conn == NULL" << endl;
        return;
    }
    // 事件类型
    bool isRead = ev.events & EPOLLIN;
    bool isWrite = ev.events & EPOLLOUT;
    bool isError = ev.events & EPOLLERR;
    // 监听 socket
    if (conn->type == Conn::TYPE::LISTEN)
    {
        if (isRead)
        {
            OnAccept(conn);
        }
    }
    // 普通 socket
    else
    {
        if (isRead || isWrite)
        {
            OnRW(conn, isRead, isWrite);
        }
        if (isError)
        {
            cout << "OnError fd:" << fd << endl;
        }
    }
}

void SocketWorker::OnAccept(shared_ptr<Conn> conn)
{
    cout << "OnAccept fd:" << conn->fd << endl;
    // 使用边缘触发模式时，需要循环 accept 直到没有更多连接
    while (true)
    {
        // 步骤1：accept
        int clientFd = accept(conn->fd, NULL, NULL);
        if (clientFd < 0)
        {
            if (errno == EAGAIN || errno == EWOULDBLOCK)
            {
                // 没有更多连接可接受
                break;
            }
            cout << "accept error: " << strerror(errno) << endl;
            break;
        }
        // 步骤2：设置非阻塞
        fcntl(clientFd, F_SETFL, O_NONBLOCK);
        // 步骤3：添加连接对象
        Sunnet::inst->AddConn(clientFd, conn->serviceId, Conn::TYPE::CLIENT);
        // 步骤4：添加到 epoll 监听列表
        struct epoll_event ev;
        ev.events = EPOLLIN | EPOLLET;
        ev.data.fd = clientFd;
        if (epoll_ctl(epollFd, EPOLL_CTL_ADD, clientFd, &ev) == -1)
        {
            cout << "OnAccept epoll_ctl fail:" << strerror(errno) << endl;
        }
        // 步骤5：通知服务
        auto msg = make_shared<SocketAcceptMsg>();
        msg->type = BaseMsg::TYPE::SOCKET_ACCEPT;
        msg->listenFd = conn->fd;
        msg->clientFd = clientFd;
        Sunnet::inst->Send(conn->serviceId, msg);
    }
}

void SocketWorker::OnRW(shared_ptr<Conn> conn, bool r, bool w)
{
    auto msg = make_shared<SocketRWMsg>();
    msg->type = BaseMsg::TYPE::SOCKET_RW;
    msg->fd = conn->fd;
    msg->isRead = r;
    msg->isWrite = w;
    Sunnet::inst->Send(conn->serviceId, msg);
}