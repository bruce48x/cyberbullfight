using System.Net;
using System.Net.Sockets;
using ServerCs.Session;

const int Port = 3010;

var listener = new TcpListener(IPAddress.Any, Port);
listener.Start();

Console.WriteLine($"[main] Server listening on port {Port}");

// Handle graceful shutdown
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("[main] Shutting down server...");
    cts.Cancel();
    listener.Stop();
};

// Register handlers
Session.RegisterHandler("connector.entryHandler.hello", (route, body) =>
{
    return new Dictionary<string, object?>
    {
        ["code"] = 0,
        ["msg"] = body
    };
});

try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        Console.WriteLine($"[main] Client connected: {client.Client.RemoteEndPoint}");

        var session = new Session(client);
        _ = session.StartAsync();
    }
}
catch (OperationCanceledException)
{
    // Expected on shutdown
}
catch (Exception ex)
{
    Console.WriteLine($"[main] Error: {ex.Message}");
}

