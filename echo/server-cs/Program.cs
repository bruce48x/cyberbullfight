using System.Net;
using ServerCs.Session;
using ServerCs.Socket;

const int Port = 3010;

var ep = new IPEndPoint(IPAddress.Any, Port);
var saeaPool = new SaeaPool(2048, 4);
async Task OnConn(IConnection conn)
{
    var sess = new Session(conn);
    _ = sess.StartAsync2();
}
var listener = new TcpListenerTransport(ep, saeaPool, OnConn);

Console.WriteLine($"[main] Server listening on port {Port}");

// Handle graceful shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("[main] Shutting down server...");
    cts.Cancel();
    listener.DisposeAsync();
};

// Register handlers
Session.RegisterHandler("connector.entryHandler.hello", (s, body) =>
{
    s.ReqId++;
    body["serverReqId"] = s.ReqId;
    return new Dictionary<string, object?>
    {
        ["code"] = 0,
        ["msg"] = body
    };
});

await listener.StartAsync(cts.Token);
