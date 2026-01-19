#pragma once
#include <memory>
#include <thread>

class Sunnet;
class Service;

using namespace std;

class Worker
{
public:
    int id;
    int eachNum;
    void operator()();
    void CheckAndPutGlobal(shared_ptr<Service> srv);
};