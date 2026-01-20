using ServerCs.Actor;
using System.Text.Json;

const int Port = 3010;

// Initialize Starnet
var starnet = new Starnet();
starnet.Start();

// Create gateway service
uint gatewayId = starnet.NewService("gateway");

// Listen on port
int listenFd = starnet.Listen(Port, gatewayId);
if (listenFd < 0)
{
    Console.WriteLine("[main] Failed to listen on port");
    return;
}

Console.WriteLine($"[main] Server listening on port {Port}");

// Register handlers
GatewayService.RegisterHandler("connector.entryHandler.hello", (session, body) =>
{
    session.ReqId++;
    var msgDict = new Dictionary<string, object?>
    {
        ["serverReqId"] = session.ReqId
    };

    if (body.ValueKind == JsonValueKind.Object)
    {
        foreach (var prop in body.EnumerateObject())
        {
            msgDict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }
    }

    var response = new Dictionary<string, object?>
    {
        ["code"] = 0,
        ["msg"] = msgDict
    };

    return JsonSerializer.Serialize(response);
});

// Wait for shutdown
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("[main] Shutting down server...");
    starnet.KillService(gatewayId);
    starnet.Stop();
};

starnet.Wait();
Console.WriteLine("[main] Server stopped");