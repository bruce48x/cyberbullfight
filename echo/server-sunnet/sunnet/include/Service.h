#pragma once
#include <queue>
#include <thread>
#include "Msg.h"

using namespace std;

class Service
{
public:
    // 唯一 id
    uint32_t id;
    // 类型
    shared_ptr<string> type;
    // 是否正在退出
    bool isExiting = false;
    // 消息队列和锁
    queue<shared_ptr<BaseMsg>> msgQueue;
    pthread_spinlock_t queueLock;
    // 构造和析构函数
    Service();
    ~Service();
    // 回调函数（编写服务逻辑）
    virtual void OnInit();
    virtual void OnMsg(shared_ptr<BaseMsg> msg);
    virtual void OnExit();
    // 插入消息
    void PushMsg(shared_ptr<BaseMsg> msg);
    // 执行消息
    bool ProcessMsg();
    void ProcessMsgs(int max);
    // 标记是否在全局队列，true: 表示在队列中，或者正在处理
    bool inGlobal = false;
    pthread_spinlock_t inGlobalLock;
    // 线程安全地设置 inGlobal
    void SetInGlobal(bool isIn);

protected:
    shared_ptr<BaseMsg> PopMsg();
    // 消息处理方法
    virtual void OnServiceMsg(shared_ptr<ServiceMsg> msg);
    virtual void OnAcceptMsg(shared_ptr<SocketAcceptMsg> msg);
    virtual void OnRWMsg(shared_ptr<SocketRWMsg> msg);
    virtual void OnSocketData(int fd, const char *buff, int len);
    virtual void OnSocketWritable(int fd);
    virtual void OnSocketClose(int fd);
};