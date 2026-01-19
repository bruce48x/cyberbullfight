#include <iostream>
#include "../sunnet/include/Sunnet.h"
#include "GatewayService.h"

using namespace std;

int main()
{
    // Disable buffering for stdout to ensure logs appear immediately in containers
    cout.setf(ios::unitbuf);
    cerr.setf(ios::unitbuf);

    new Sunnet();
    Sunnet::inst->Start();

    // Register handler
    GatewayService::register_handler("connector.entryHandler.hello",
        [](Session& s, json body) {
            s.ReqId++;
            body["serverReqId"] = s.ReqId;
            json resp;
            resp["code"] = 0;
            resp["msg"] = body;
            return resp.dump();
        });

    // Create gateway service
    auto gatewayType = make_shared<string>("gateway");
    uint32_t gatewayId = Sunnet::inst->NewService(gatewayType);

    // Listen on port 3010
    int listenFd = Sunnet::inst->Listen(3010, gatewayId);
    if (listenFd < 0) {
        cerr << "[main] Failed to listen on port 3010" << endl;
        return 1;
    }
    cout << "[main] Server listening on port 3010" << endl;

    Sunnet::inst->Wait();
    return 0;
}
