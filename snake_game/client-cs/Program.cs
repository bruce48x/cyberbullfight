using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using SnakeGame.Client.Protocol;

// Multiplayer client for the Snake server using Pomelo protocol.

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 5000;
var playerName = args.Length > 2 ? args[2] : (Environment.UserName ?? "Player");

Console.CursorVisible = false;
Console.Clear();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

Console.WriteLine($"Connecting to {host}:{port}...");
using var tcp = new TcpClient();
try
{
    await tcp.ConnectAsync(host, port);
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
    return;
}

using var stream = tcp.GetStream();

var latestState = new ServerState();
var myId = -1;
var running = true;
var receivedWelcome = false;
var stateLock = new object();
var buffer = new List<byte>();
var readBuffer = new byte[4096];

// Send handshake
var handshakeData = new Dictionary<string, object?>
{
    ["name"] = playerName
};
var handshakeBody = JsonSerializer.SerializeToUtf8Bytes(handshakeData);
var handshakePkg = Package.Encode(PackageType.Handshake, handshakeBody);
await stream.WriteAsync(handshakePkg, 0, handshakePkg.Length);
await stream.FlushAsync();

// Wait for handshake response
while (!receivedWelcome)
{
    var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
    if (bytesRead == 0)
    {
        Console.WriteLine("Connection closed by server");
        return;
    }

    buffer.AddRange(readBuffer.Take(bytesRead));

    var pkg = Package.Decode(buffer.ToArray());
    if (pkg == null) continue;

    if (pkg.Type == PackageType.Handshake)
    {
        var response = JsonSerializer.Deserialize<Dictionary<string, object?>>(pkg.Body, jsonOptions);
        if (response != null && response.TryGetValue("user", out var userObj))
        {
            if (userObj is JsonElement user)
            {
                if (user.TryGetProperty("id", out var idElem))
                    myId = idElem.GetInt32();
                
                if (user.TryGetProperty("width", out var widthElem))
                    latestState.Width = widthElem.GetInt32();
                
                if (user.TryGetProperty("height", out var heightElem))
                    latestState.Height = heightElem.GetInt32();
            }
        }

        // Send handshake ack
        var ackPkg = Package.Encode(PackageType.HandshakeAck, null);
        await stream.WriteAsync(ackPkg, 0, ackPkg.Length);
        await stream.FlushAsync();

        buffer.Clear();
        receivedWelcome = true;
        break;
    }
    else
    {
        Console.WriteLine($"Unexpected package type: {pkg.Type}");
        return;
    }
}

// Start listening task for messages
var listenTask = Task.Run(async () =>
{
    try
    {
        while (running)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
            {
                running = false;
                break;
            }

            buffer.AddRange(readBuffer.Take(bytesRead));

            while (true)
            {
                var pkg = Package.Decode(buffer.ToArray());
                if (pkg == null) break;

                var pkgSize = 4 + pkg.Length;
                buffer.RemoveRange(0, pkgSize);

                await ProcessPackageAsync(pkg);
            }
        }
    }
    catch (Exception ex)
    {
        running = false;
        Console.SetCursorPosition(0, 25);
        Console.WriteLine($"Connection error: {ex.Message}");
    }
});

// Input task
var inputTask = Task.Run(async () =>
{
    while (running)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            var dir = key switch
            {
                ConsoleKey.W => "Up",
                ConsoleKey.A => "Left",
                ConsoleKey.S => "Down",
                ConsoleKey.D => "Right",
                _ => null
            };

            if (dir is not null)
            {
                await SendDirectionAsync(stream, dir);
            }
        }
        await Task.Delay(10);
    }
});

// Render loop
while (running)
{
    ServerState snapshot;
    int idSnapshot;
    lock (stateLock)
    {
        snapshot = latestState;
        idSnapshot = myId;
    }

    Draw(snapshot, idSnapshot);
    await Task.Delay(80);
}

Console.SetCursorPosition(0, (latestState?.Height ?? 18) + 4);
Console.WriteLine("Disconnected. Press any key to exit.");
Console.ReadKey(true);

await Task.WhenAll(listenTask, inputTask);
return;

// --- Local helpers ---

async Task ProcessPackageAsync(Package pkg)
{
    switch (pkg.Type)
    {
        case PackageType.Heartbeat:
            // Send heartbeat response
            var heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
            await stream.WriteAsync(heartbeatPkg, 0, heartbeatPkg.Length);
            await stream.FlushAsync();
            break;

        case PackageType.Data:
            var msg = Message.Decode(pkg.Body);
            if (msg == null) return;

            if (msg.Type == MessageType.Push && msg.Route == "snake.state")
            {
                try
                {
                    var state = JsonSerializer.Deserialize<ServerState>(msg.Body, jsonOptions);
                    if (state != null)
                    {
                        lock (stateLock)
                        {
                            latestState = state;
                        }
                    }
                }
                catch { }
            }
            break;
    }
}

async Task SendDirectionAsync(NetworkStream stream, string direction)
{
    var body = new Dictionary<string, object?>
    {
        ["dir"] = direction
    };
    var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);
    var notifyMsg = Message.Encode(0, MessageType.Notify, false, "snake.move", bodyBytes);
    var dataPkg = Package.Encode(PackageType.Data, notifyMsg);
    await stream.WriteAsync(dataPkg, 0, dataPkg.Length);
    await stream.FlushAsync();
}

void Draw(ServerState state, int myId)
{
    var width = state.Width > 0 ? state.Width : 32;
    var height = state.Height > 0 ? state.Height : 18;
    var sb = new StringBuilder();

    sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();

    for (var y = 0; y < height; y++)
    {
        sb.Append('|');
        for (var x = 0; x < width; x++)
        {
            var pos = new Pos(x, y);
            char cell = ' ';

            if (state.Foods.Any(f => f.Equals(pos)))
            {
                cell = '@';
            }

            foreach (var p in state.Players)
            {
                if (!p.Alive) continue;
                if (p.Segments.Count == 0) continue;
                if (p.Segments[0].Equals(pos))
                {
                    cell = p.Id == myId ? 'O' : 'Q';
                    break;
                }
                if (p.Segments.Skip(1).Any(s => s.Equals(pos)))
                {
                    cell = p.Id == myId ? 'o' : 'q';
                }
            }

            sb.Append(cell);
        }
        sb.Append('|').AppendLine();
    }

    sb.Append('+').Append(new string('-', width)).Append('+').AppendLine();
    sb.Append($"You: {playerName} (id {myId})  Controls: WASD | Players: ");
    foreach (var p in state.Players)
    {
        sb.Append($"{p.Name}[{p.Score}] ");
    }

    Console.SetCursorPosition(0, 0);
    Console.Write(sb.ToString());
}

// --- Types ---

record struct Pos(int X, int Y);

enum Direction
{
    Up,
    Down,
    Left,
    Right
}

class ServerState
{
    public int Tick { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<Pos> Foods { get; set; } = new();
    public List<PlayerView> Players { get; set; } = new();
}

class PlayerView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Alive { get; set; }
    public int Score { get; set; }
    public Direction Direction { get; set; }
    public List<Pos> Segments { get; set; } = new();
}
