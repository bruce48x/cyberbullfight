#include "Service.h"
#include "Sunnet.h"
#include <iostream>
#include <sys/socket.h>

Service::Service()
{
    // 初始化锁
    pthread_spin_init(&queueLock, PTHREAD_PROCESS_PRIVATE);
    pthread_spin_init(&inGlobalLock, PTHREAD_PROCESS_PRIVATE);
}

Service::~Service()
{
    pthread_spin_destroy(&queueLock);
    pthread_spin_destroy(&inGlobalLock);
}

void Service::PushMsg(shared_ptr<BaseMsg> msg)
{
    pthread_spin_lock(&queueLock);
    {
        msgQueue.push(msg);
    }
    pthread_spin_unlock(&queueLock);
}

shared_ptr<BaseMsg> Service::PopMsg()
{
    shared_ptr<BaseMsg> msg = NULL;
    pthread_spin_lock(&queueLock);
    {
        if (!msgQueue.empty())
        {
            msg = msgQueue.front();
            msgQueue.pop();
        }
    }
    pthread_spin_unlock(&queueLock);
    return msg;
}

void Service::OnInit()
{
    cout << "[" << id << "] OnInit" << endl;
}

void Service::OnMsg(shared_ptr<BaseMsg> msg)
{
    if (msg->type == BaseMsg::TYPE::SERVICE)
    {
        auto m = dynamic_pointer_cast<ServiceMsg>(msg);
        OnServiceMsg(m);
    }
    else if (msg->type == BaseMsg::TYPE::SOCKET_ACCEPT)
    {
        auto m = dynamic_pointer_cast<SocketAcceptMsg>(msg);
        OnAcceptMsg(m);
    }
    else if (msg->type == BaseMsg::TYPE::SOCKET_RW)
    {
        auto m = dynamic_pointer_cast<SocketRWMsg>(msg);
        OnRWMsg(m);
    }
}

void Service::OnExit()
{
    cout << "[" << id << "] OnExit" << endl;
}

bool Service::ProcessMsg()
{
    shared_ptr<BaseMsg> msg = PopMsg();
    if (msg)
    {
        OnMsg(msg);
        return true;
    }
    else
    {
        return false;
    }
}

void Service::ProcessMsgs(int max)
{
    for (int i = 0; i < max; i++)
    {
        bool succ = ProcessMsg();
        if (!succ)
        {
            break;
        }
    }
}

void Service::SetInGlobal(bool isIn)
{
    pthread_spin_lock(&inGlobalLock);
    {
        inGlobal = isIn;
    }
    pthread_spin_unlock(&inGlobalLock);
}

void Sunnet::Send(uint32_t toId, shared_ptr<BaseMsg> msg)
{
    shared_ptr<Service> toSrv = GetService(toId);
    if (!toSrv)
    {
        cout << "Send fail, toSrv not exist toId:" << toId << endl;
        return;
    }
    // 插入目标服务的消息队列
    toSrv->PushMsg(msg);
    // 检查并放入全局队列
    bool hasPush = false;
    pthread_spin_lock(&toSrv->inGlobalLock);
    {
        if (!toSrv->inGlobal)
        {
            PushGlobalQueue(toSrv);
            toSrv->inGlobal = true;
            hasPush = true;
        }
    }
    pthread_spin_unlock(&toSrv->inGlobalLock);
    // 唤醒进程，不放在临界区里面
    if (hasPush)
    {
        CheckAndWakeUp();
    }
}

void Service::OnServiceMsg(shared_ptr<ServiceMsg> msg)
{
    cout << "OnServiceMsg" << endl;
}
void Service::OnAcceptMsg(shared_ptr<SocketAcceptMsg> msg)
{
    cout << "OnAcceptMsg " << msg->clientFd << endl;
}
void Service::OnRWMsg(shared_ptr<SocketRWMsg> msg)
{
    int fd = msg->fd;
    // 可读
    if (msg->isRead)
    {
        const int BUFFSIZE = 512;
        char buff[BUFFSIZE];
        int len = 0;
        do
        {
            len = read(fd, &buff, BUFFSIZE);
            if (len > 0)
            {
                OnSocketData(fd, buff, len);
            }
        } while (len == BUFFSIZE);

        if (len <= 0 && errno != EAGAIN)
        {
            if (Sunnet::inst->GetConn(fd))
            {
                OnSocketClose(fd);
                Sunnet::inst->CloseConn(fd);
            }
        }
    }
    // 可写（注意没有else)
    if (msg->isWrite)
    {
        if (Sunnet::inst->GetConn(fd))
        {
            OnSocketWritable(fd);
        }
    }
}

void Service::OnSocketData(int fd, const char *buff, int len)
{
    cout << "OnSocketData" << fd << " buff: " << buff << endl;
    char writeBuff[3] = {'l', 'p', 'y'};
    write(fd, &writeBuff, 3);
}

void Service::OnSocketWritable(int fd)
{
    cout << "OnSocketWritable " << fd << endl;
}

void Service::OnSocketClose(int fd)
{
    cout << "OnSocketClose " << fd << endl;
}