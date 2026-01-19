#pragma once
#include <memory>
using namespace std;

class BaseMsg
{
public:
    enum TYPE
    {
        SERVICE = 1,
        SOCKET_ACCEPT = 2,
        SOCKET_RW = 3,
    };
    uint8_t type;
    char load[999999]{};
    virtual ~BaseMsg() {};
};

class ServiceMsg : public BaseMsg
{
public:
    uint32_t source;       // 消息发送方
    shared_ptr<char> buff; // 消息内容
    size_t size;           // 消息内容大小
};

class SocketAcceptMsg : public BaseMsg
{
public:
    int listenFd;
    int clientFd;
};

class SocketRWMsg : public BaseMsg
{
public:
    int fd;
    bool isRead = false;
    bool isWrite = false;
};