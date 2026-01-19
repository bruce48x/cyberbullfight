#include <iostream>
#include <unistd.h>
#include "Worker.h"
#include "Service.h"
#include "Sunnet.h"

using namespace std;

void Worker::operator()()
{
    while (true)
    {
        shared_ptr<Service> srv = Sunnet::inst->PopGlobalQueue();
        if (!srv)
        {
            Sunnet::inst->WorkerWait();
        }
        else
        {
            srv->ProcessMsgs(eachNum);
            CheckAndPutGlobal(srv);
        }
    }
}

void Worker::CheckAndPutGlobal(shared_ptr<Service> srv)
{
    if (srv->isExiting)
    {
        return;
    }

    pthread_spin_lock(&srv->queueLock);
    {
        // 重新放回全局队列
        if (!srv->msgQueue.empty())
        {
            // 此时 srv->inGlobal 一定是 true
            Sunnet::inst->PushGlobalQueue(srv);
        }
        else
        {
            // 不在队列中，重设 inGlobal
            srv->SetInGlobal(false);
        }
    }
    pthread_spin_unlock(&srv->queueLock);
}