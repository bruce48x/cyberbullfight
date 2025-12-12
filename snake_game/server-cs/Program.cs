using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using SnakeGame.Server.Protocol;

// Snake server using Pomelo protocol. Accepts TCP clients, runs an authoritative
// game loop, and broadcasts world state.

var width = 32;
var height = 18;
var tick = TimeSpan.FromMilliseconds(160);
var listener = new TcpListener(IPAddress.Any, 5000);
listener.Start();

Console.WriteLine("Snake server listening on 0.0.0.0:5000");

var stateLock = new object();
var rng = new Random();
var players = new Dictionary<int, Player>();
var foods = new List<Pos>();
var nextId = 1;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

// Pre-spawn a food.
EnsureFood();

// Start game loop.
_ = Task.Run(GameLoop);

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleClient(client));
}

// --- Local functions ---

async Task GameLoop()
{
    while (true)
    {
        await Task.Delay(tick);
        List<Player> snapshot;
        List<Pos> foodSnapshot;
        lock (stateLock)
        {
            AdvanceWorld();
            snapshot = players.Values.Select(p => p.Clone()).ToList();
            foodSnapshot = foods.ToList();
        }

        var state = new ServerState
        {
            Tick = Environment.TickCount,
            Width = width,
            Height = height,
            Foods = foodSnapshot,
            Players = snapshot.Select(p => p.ToView()).ToList()
        };

        BroadcastState(state);
    }
}

async Task HandleClient(TcpClient socket)
{
    using var client = socket;
    var stream = client.GetStream();
    
    Player? player = null;
    var buffer = new List<byte>();
    var readBuffer = new byte[4096];

    try
    {
        // Wait for handshake
        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
            {
                Console.WriteLine("Client disconnected before handshake");
                return;
            }

            buffer.AddRange(readBuffer.Take(bytesRead));

            // Try to decode package
            var pkg = Package.Decode(buffer.ToArray());
            if (pkg == null)
            {
                // Not enough data, continue reading
                if (buffer.Count > 1024 * 10) // Prevent buffer overflow
                {
                    Console.WriteLine("Buffer overflow");
                    return;
                }
                continue;
            }

            // Process handshake
            if (pkg.Type == PackageType.Handshake)
            {
                var handshakeData = JsonSerializer.Deserialize<Dictionary<string, object?>>(pkg.Body, jsonOptions);
                var playerName = handshakeData?.TryGetValue("name", out var name) == true 
                    ? name?.ToString() 
                    : null;

                lock (stateLock)
                {
                    var id = nextId++;
                    player = CreatePlayer(id, playerName ?? $"Player{id}");
                    player.Stream = stream;
                    players[id] = player;
                }

                // Send handshake response
                var handshakeResponse = new Dictionary<string, object?>
                {
                    ["code"] = 200,
                    ["sys"] = new Dictionary<string, object?>
                    {
                        ["heartbeat"] = 10,
                        ["dict"] = new Dictionary<string, object?>(),
                        ["protos"] = new Dictionary<string, object?>
                        {
                            ["client"] = new Dictionary<string, object?>(),
                            ["server"] = new Dictionary<string, object?>()
                        }
                    },
                    ["user"] = new Dictionary<string, object?>
                    {
                        ["id"] = player.Id,
                        ["width"] = width,
                        ["height"] = height
                    }
                };

                var responseBody = JsonSerializer.SerializeToUtf8Bytes(handshakeResponse);
                var responsePkg = Package.Encode(PackageType.Handshake, responseBody);
                await stream.WriteAsync(responsePkg, 0, responsePkg.Length);
                await stream.FlushAsync();

                buffer.Clear();
                Console.WriteLine($"Player {player.Id} ({player.Name}) connected");
                break;
            }
            else
            {
                Console.WriteLine($"Unexpected package type during handshake: {pkg.Type}");
                return;
            }
        }

        // Wait for handshake ack
        buffer.Clear();
        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0)
            {
                Console.WriteLine("Client disconnected before handshake ack");
                return;
            }

            buffer.AddRange(readBuffer.Take(bytesRead));

            var pkg = Package.Decode(buffer.ToArray());
            if (pkg == null) continue;

            if (pkg.Type == PackageType.HandshakeAck)
            {
                buffer.Clear();
                break;
            }
            else
            {
                Console.WriteLine($"Unexpected package type: {pkg.Type}");
                return;
            }
        }

        // Send initial state
        List<Player> snapshot;
        List<Pos> foodSnapshot;
        lock (stateLock)
        {
            snapshot = players.Values.Select(p => p.Clone()).ToList();
            foodSnapshot = foods.ToList();
        }

        var initialState = new ServerState
        {
            Tick = Environment.TickCount,
            Width = width,
            Height = height,
            Foods = foodSnapshot,
            Players = snapshot.Select(p => p.ToView()).ToList()
        };

        await SendStateAsync(stream, initialState);

        // Main message loop
        buffer.Clear();
        while (true)
        {
            var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            if (bytesRead == 0) break;

            buffer.AddRange(readBuffer.Take(bytesRead));

            while (true)
            {
                var pkg = Package.Decode(buffer.ToArray());
                if (pkg == null) break;

                // Remove processed package from buffer
                var pkgSize = 4 + pkg.Length;
                buffer.RemoveRange(0, pkgSize);

                await ProcessPackageAsync(pkg, player!);
            }
        }
    }
    catch (IOException)
    {
        // Client disconnected.
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client error: {ex}");
    }
    finally
    {
        if (player is not null)
        {
            lock (stateLock)
            {
                players.Remove(player.Id);
            }
            Console.WriteLine($"Player {player.Id} disconnected");
        }
    }
}

async Task ProcessPackageAsync(Package pkg, Player player)
{
    switch (pkg.Type)
    {
        case PackageType.Heartbeat:
            // Send heartbeat response
            var heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
            await player.Stream!.WriteAsync(heartbeatPkg, 0, heartbeatPkg.Length);
            await player.Stream.FlushAsync();
            break;

        case PackageType.Data:
            var msg = Message.Decode(pkg.Body);
            if (msg == null) return;

            if (msg.Type == MessageType.Notify && msg.Route == "snake.move")
            {
                Dictionary<string, object?>? body = null;
                if (msg.Body.Length > 0)
                {
                    try
                    {
                        body = JsonSerializer.Deserialize<Dictionary<string, object?>>(msg.Body, jsonOptions);
                    }
                    catch { }
                }

                if (body != null && body.TryGetValue("dir", out var dirObj))
                {
                    if (Enum.TryParse<Direction>(dirObj?.ToString(), true, out var dir))
                    {
                        lock (stateLock)
                        {
                            if (players.TryGetValue(player.Id, out var p))
                            {
                                if (!IsOpposite(p.Direction, dir))
                                {
                                    p.Pending = dir;
                                }
                            }
                        }
                    }
                }
            }
            break;
    }
}

async Task SendStateAsync(NetworkStream stream, ServerState state)
{
    var stateData = JsonSerializer.SerializeToUtf8Bytes(state);
    var pushMsg = Message.Encode(0, MessageType.Push, false, "snake.state", stateData);
    var dataPkg = Package.Encode(PackageType.Data, pushMsg);
    await stream.WriteAsync(dataPkg, 0, dataPkg.Length);
    await stream.FlushAsync();
}

void BroadcastState(ServerState state)
{
    var stateData = JsonSerializer.SerializeToUtf8Bytes(state);
    var pushMsg = Message.Encode(0, MessageType.Push, false, "snake.state", stateData);
    var dataPkg = Package.Encode(PackageType.Data, pushMsg);

    List<Player> targets;
    lock (stateLock)
    {
        targets = players.Values.ToList();
    }

    var failed = new List<int>();
    foreach (var p in targets)
    {
        try
        {
            if (p.Stream != null)
            {
                p.Stream.WriteAsync(dataPkg, 0, dataPkg.Length).Wait();
                p.Stream.FlushAsync().Wait();
            }
        }
        catch
        {
            failed.Add(p.Id);
        }
    }

    if (failed.Count > 0)
    {
        lock (stateLock)
        {
            foreach (var id in failed)
            {
                players.Remove(id);
            }
        }
    }
}

void AdvanceWorld()
{
    EnsureFood();
    if (players.Count == 0) return;

    // Build occupancy map.
    var occupancy = new HashSet<Pos>();
    foreach (var p in players.Values.Where(p => p.Alive))
    {
        if (p.Segments.Count == 0) continue;
        foreach (var seg in p.Segments)
        {
            occupancy.Add(seg);
        }
    }

    foreach (var p in players.Values.Where(p => p.Alive))
    {
        if (p.Segments.Count == 0) continue;
        var lastNode = p.Segments.Last;
        var firstNode = p.Segments.First;
        if (lastNode is null || firstNode is null) continue;
        
        // Allow moving into tail since it will vacate.
        occupancy.Remove(lastNode.Value);

        if (!IsOpposite(p.Direction, p.Pending))
        {
            p.Direction = p.Pending;
        }

        var nextHead = Step(firstNode.Value, p.Direction);
        var hitWall = nextHead.X < 0 || nextHead.X >= width || nextHead.Y < 0 || nextHead.Y >= height;
        var hitBody = occupancy.Contains(nextHead);
        if (hitWall || hitBody)
        {
            p.Alive = false;
            p.Segments.Clear();
            continue;
        }

        var ate = false;
        for (var i = 0; i < foods.Count; i++)
        {
            if (foods[i].Equals(nextHead))
            {
                foods.RemoveAt(i);
                ate = true;
                p.Score += 1;
                break;
            }
        }

        p.Segments.AddFirst(nextHead);
        if (!ate)
        {
            p.Segments.RemoveLast();
        }
        occupancy.Add(nextHead);
    }

    // Respawn food if needed.
    EnsureFood();
}

void EnsureFood()
{
    const int targetFood = 1;
    while (foods.Count < targetFood)
    {
        var candidate = new Pos(rng.Next(0, width), rng.Next(0, height));
        if (players.Values.Any(p => p.Segments.Any(s => s.Equals(candidate)))) continue;
        foods.Add(candidate);
    }
}

Player CreatePlayer(int id, string name)
{
    Pos origin;
    int attempts = 0;
    while (true)
    {
        origin = new Pos(rng.Next(2, width - 2), rng.Next(2, height - 2));
        var body = new[]
        {
            origin,
            origin with { X = origin.X - 1 },
            origin with { X = origin.X - 2 }
        };

        var collision = players.Values.Any(p => p.Segments.Any(s => body.Contains(s)));
        if (!collision || attempts++ > 100)
        {
            break;
        }
    }

    var segs = new LinkedList<Pos>();
    segs.AddFirst(origin);
    segs.AddLast(origin with { X = origin.X - 1 });
    segs.AddLast(origin with { X = origin.X - 2 });

    return new Player
    {
        Id = id,
        Name = name,
        Direction = Direction.Right,
        Pending = Direction.Right,
        Segments = segs
    };
}

Pos Step(Pos p, Direction d) =>
    d switch
    {
        Direction.Up => p with { Y = p.Y - 1 },
        Direction.Down => p with { Y = p.Y + 1 },
        Direction.Left => p with { X = p.X - 1 },
        Direction.Right => p with { X = p.X + 1 },
        _ => p
    };

bool IsOpposite(Direction a, Direction b) =>
    (a, b) switch
    {
        (Direction.Up, Direction.Down) => true,
        (Direction.Down, Direction.Up) => true,
        (Direction.Left, Direction.Right) => true,
        (Direction.Right, Direction.Left) => true,
        _ => false
    };

// --- Types ---

class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "Player";
    public bool Alive { get; set; } = true;
    public int Score { get; set; }
    public Direction Direction { get; set; }
    public Direction Pending { get; set; }
    public LinkedList<Pos> Segments { get; set; } = new();
    public NetworkStream? Stream { get; set; }

    public Player Clone() => new()
    {
        Id = Id,
        Name = Name,
        Alive = Alive,
        Score = Score,
        Direction = Direction,
        Pending = Pending,
        Segments = new LinkedList<Pos>(Segments.Select(s => s))
    };

    public PlayerView ToView() => new()
    {
        Id = Id,
        Name = Name,
        Alive = Alive,
        Score = Score,
        Direction = Direction,
        Segments = Segments.ToList()
    };
}

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
