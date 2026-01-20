#include <iostream>
#include "Sunnet.h"
#include <cassert>
#include <unistd.h>
#include <fcntl.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
#include <signal.h>
#include <mutex>

using namespace std;

Sunnet *Sunnet::inst;

// 使用函数局部静态变量避免静态初始化顺序问题
unordered_map<string, std::function<shared_ptr<Service>()>>& Sunnet::GetServiceCreators()
{
    static unordered_map<string, std::function<shared_ptr<Service>()>> serviceCreators;
    return serviceCreators;
}

pthread_rwlock_t& Sunnet::GetServiceCreatorsLock()
{
    static pthread_rwlock_t serviceCreatorsLock;
    static std::once_flag init_flag;
    std::call_once(init_flag, []() {
        pthread_rwlock_init(&serviceCreatorsLock, NULL);
    });
    return serviceCreatorsLock;
}
Sunnet::Sunnet()
{
    inst = this;
}

void Sunnet::Start()
{
    cout << "hello Sunnet" << endl;

    // 忽略 SIGPIPE 信号（避免 write 失效的 fd 的情况下，系统发送 PIPE 信号，导致进程退出）
    signal(SIGPIPE, SIG_IGN);

    // 锁
    pthread_rwlock_init(&servicesLock, NULL);
    pthread_spin_init(&globalLock, PTHREAD_PROCESS_PRIVATE);
    pthread_cond_init(&sleepCond, NULL);
    pthread_mutex_init(&sleepMtx, NULL);
    assert(pthread_rwlock_init(&connsLock, NULL) == 0);
    // 开启 worker
    StartWorker();
    StartSocket();
}

void Sunnet::StartWorker()
{
    for (int i = 0; i < WORKER_NUM; i++)
    {
        cout << "start worker thread:" << i << endl;
        Worker *worker = new Worker();
        worker->id = i;
        worker->eachNum = 2 << i;
        thread *wt = new thread(*worker);
        workers.push_back(worker);
        workerThreads.push_back(wt);
    }
}

void Sunnet::StartSocket()
{
    socketWorker = new SocketWorker();
    socketWorker->Init();
    socketThread = new thread(*socketWorker);
}

void Sunnet::Wait()
{
    if (workerThreads[0])
    {
        workerThreads[0]->join();
    }
}

void Sunnet::RegisterService(const string& type, std::function<shared_ptr<Service>()> creator)
{
    auto& serviceCreators = GetServiceCreators();
    auto& serviceCreatorsLock = GetServiceCreatorsLock();
    pthread_rwlock_wrlock(&serviceCreatorsLock);
    {
        serviceCreators[type] = creator;
    }
    pthread_rwlock_unlock(&serviceCreatorsLock);
}

uint32_t Sunnet::NewService(shared_ptr<string> type)
{
    shared_ptr<Service> srv;
    
    // 查找注册的服务创建器
    auto& serviceCreators = GetServiceCreators();
    auto& serviceCreatorsLock = GetServiceCreatorsLock();
    pthread_rwlock_rdlock(&serviceCreatorsLock);
    {
        auto iter = serviceCreators.find(*type);
        if (iter != serviceCreators.end()) {
            // 使用注册的创建器
            srv = iter->second();
        } else {
            // 默认创建基础 Service
            srv = make_shared<Service>();
        }
    }
    pthread_rwlock_unlock(&serviceCreatorsLock);
    
    srv->type = type;
    pthread_rwlock_wrlock(&servicesLock);
    {
        srv->id = maxId;
        maxId++;
        services.emplace(srv->id, srv);
    }
    pthread_rwlock_unlock(&servicesLock);
    srv->OnInit();
    return srv->id;
}

shared_ptr<Service> Sunnet::GetService(uint32_t id)
{
    shared_ptr<Service> srv = NULL;
    pthread_rwlock_rdlock(&servicesLock);
    {
        unordered_map<uint32_t, shared_ptr<Service>>::iterator iter = services.find(id);
        if (iter != services.end())
        {
            srv = iter->second;
        }
    }
    pthread_rwlock_unlock(&servicesLock);
    return srv;
}

// 删除服务
// 只能 service 自己调自己，因为会调用不加锁的 OnExit 和 isExiting
void Sunnet::KillService(uint32_t id)
{
    shared_ptr<Service> srv = GetService(id);
    if (!srv)
    {
        return;
    }
    srv->OnExit();
    srv->isExiting = true;
    // 删列表
    pthread_rwlock_wrlock(&servicesLock);
    {
        services.erase(id);
    }
    pthread_rwlock_unlock(&servicesLock);
}

shared_ptr<Service> Sunnet::PopGlobalQueue()
{
    shared_ptr<Service> srv = NULL;
    pthread_spin_lock(&globalLock);
    {
        if (!globalQueue.empty())
        {
            srv = globalQueue.front();
            globalQueue.pop();
            globalLen--;
        }
    }
    pthread_spin_unlock(&globalLock);
    return srv;
}

void Sunnet::PushGlobalQueue(shared_ptr<Service> srv)
{
    pthread_spin_lock(&globalLock);
    {
        globalQueue.push(srv);
        globalLen++;
    }
    pthread_spin_unlock(&globalLock);
}

// 仅测试用，buff 须由 new 产生
shared_ptr<BaseMsg> Sunnet::MakeMsg(uint32_t source, char *buff, int len)
{
    auto msg = make_shared<ServiceMsg>();
    msg->type = BaseMsg::TYPE::SERVICE;
    msg->source = source;
    // 基本类型的对象没有析构函数
    // 所以用 delete 或 delete[] 都可以销毁基本类型数组
    // 智能指针默认使用 delete 销毁对象，
    // 所以无须重写智能指针的销毁方法
    msg->buff = shared_ptr<char>(buff);
    msg->size = len;
    return msg;
}

// Worker 线程调用，进入休眠
void Sunnet::WorkerWait()
{
    pthread_mutex_lock(&sleepMtx);
    sleepCount++;
    pthread_cond_wait(&sleepCond, &sleepMtx);
    sleepCount--;
    pthread_mutex_unlock(&sleepMtx);
}

// 检查并唤醒线程
void Sunnet::CheckAndWakeUp()
{
    // unsafe
    if (sleepCount == 0)
    {
        return;
    }
    if (WORKER_NUM - sleepCount <= globalLen)
    {
        pthread_cond_signal(&sleepCond);
    }
}

// 添加连接
int Sunnet::AddConn(int fd, uint32_t id, Conn::TYPE type)
{
    auto conn = make_shared<Conn>();
    conn->fd = fd;
    conn->serviceId = id;
    conn->type = type;
    pthread_rwlock_wrlock(&connsLock);
    {
        conns.emplace(fd, conn);
    }
    pthread_rwlock_unlock(&connsLock);
    return fd;
}

shared_ptr<Conn> Sunnet::GetConn(int fd)
{
    shared_ptr<Conn> conn = NULL;
    pthread_rwlock_rdlock(&connsLock);
    {
        unordered_map<uint32_t, shared_ptr<Conn>>::iterator iter = conns.find(fd);
        if (iter != conns.end())
        {
            conn = iter->second;
        }
    }
    pthread_rwlock_unlock(&connsLock);
    return conn;
}

bool Sunnet::RemoveConn(int fd)
{
    int result;
    pthread_rwlock_wrlock(&connsLock);
    {
        result = conns.erase(fd);
    }
    pthread_rwlock_unlock(&connsLock);
    return result == 1;
}

int Sunnet::Listen(uint32_t port, uint32_t serviceId)
{
    // 步骤1：创建 socket
    int listenFd = socket(AF_INET, SOCK_STREAM, 0);
    if (listenFd <= 0)
    {
        cout << "listen error, listenFd <= 0" << endl;
        return -1;
    }
    // 步骤2：设置为非阻塞
    fcntl(listenFd, F_SETFL, O_NONBLOCK);
    // 步骤3：bind
    struct sockaddr_in addr; // 创建地址结构
    addr.sin_family = AF_INET;
    addr.sin_port = htons(port);
    addr.sin_addr.s_addr = htonl(INADDR_ANY);
    int r = bind(listenFd, (struct sockaddr *)&addr, sizeof(addr));
    if (r == -1)
    {
        return -1;
    }
    // 步骤4：listen
    r = listen(listenFd, 64);
    if (r < 0)
    {
        return -1;
    }
    // 步骤5：添加到管理接口
    AddConn(listenFd, serviceId, Conn::TYPE::LISTEN);
    // 步骤6：epoll 事件，跨线程
    socketWorker->AddEvent(listenFd);

    return listenFd;
}

void Sunnet::CloseConn(uint32_t fd)
{
    bool succ = RemoveConn(fd);
    close(fd);
    if (succ)
    {
        socketWorker->RemoveEvent(fd);
    }
}