using ServerCs.Actor;
using System.Text.Json;

const int Port = 3010;

// Initialize Starnet
var starnet = new Starnet();
starnet.Start();

// Create gateway service
var gatewayService = new GatewayService
{
    Port = Port
};
uint gatewayId = starnet.NewService("gateway", gatewayService);

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