#pragma once
#include <vector>
#include "Worker.h"
#include "Service.h"
#include "SocketWorker.h"
#include "Conn.h"
#include <unordered_map>

class GatewayService; // Forward declaration

class Sunnet
{
public:
    static Sunnet *inst;

public:
    Sunnet();
    void Start();
    void Wait();
    // 服务列表
    unordered_map<uint32_t, shared_ptr<Service>> services;
    uint32_t maxId = 0;            // 最大ID
    pthread_rwlock_t servicesLock; // 读写锁
    // 增删服务
    uint32_t NewService(shared_ptr<string> type);
    void KillService(uint32_t id); // 仅限服务自己条用
    // 发送消息
    void Send(uint32_t toId, shared_ptr<BaseMsg> msg);
    // 全局队列操作
    shared_ptr<Service> PopGlobalQueue();
    void PushGlobalQueue(shared_ptr<Service> srv);
    // 辅助函数
    shared_ptr<BaseMsg> MakeMsg(uint32_t source, char *buff, int len);
    // 唤醒工作线程
    void CheckAndWakeUp();
    // 让工作线程等待（仅工作线程调用）
    void WorkerWait();
    int AddConn(int fd, uint32_t id, Conn::TYPE type);
    shared_ptr<Conn> GetConn(int fd);
    bool RemoveConn(int fd);
    // 网络连接操作接口
    int Listen(uint32_t port, uint32_t serviceId);
    void CloseConn(uint32_t fd);

private:
    int WORKER_NUM = 3;
    vector<Worker *> workers;
    vector<thread *> workerThreads;
    void StartWorker();
    // 获取服务
    shared_ptr<Service> GetService(uint32_t id);
    // 全局队列
    queue<shared_ptr<Service>> globalQueue;
    int globalLen = 0;             // 队列长度
    pthread_spinlock_t globalLock; // 全局锁
    // 休眠和唤醒
    pthread_mutex_t sleepMtx;
    pthread_cond_t sleepCond;
    int sleepCount = 0; // 休眠工作线程数
    // Socket 线程
    SocketWorker *socketWorker;
    thread *socketThread;
    void StartSocket();
    // Conn 列表
    unordered_map<uint32_t, shared_ptr<Conn>> conns;
    pthread_rwlock_t connsLock;
};