using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;
using SnakeGame.Client.Protocol;

// Multiplayer client for the Snake server using Pomelo protocol.
// Use environment variable CLIENT_MODE=ai to enable AI mode, otherwise use manual control.

var isAiMode = Environment.GetEnvironmentVariable("CLIENT_MODE")?.ToLower() == "ai";

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 5000;
var playerName = args.Length > 2 ? args[2] : (isAiMode ? $"AI_{Random.Shared.Next(1000, 9999)}" : (Environment.UserName ?? "Player"));

if (!isAiMode)
{
    Console.CursorVisible = false;
    Console.Clear();
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var logPrefix = isAiMode ? $"[{DateTime.Now:HH:mm:ss}]" : "";
Console.WriteLine($"{logPrefix} Connecting to {host}:{port} as {playerName}...");
using var tcp = new TcpClient();
try
{
    await tcp.ConnectAsync(host, port);
}
catch (Exception ex)
{
    Console.WriteLine($"{logPrefix} Failed to connect: {ex.Message}");
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

// 游戏状态跟踪（AI模式使用）
var lastScore = 0;
var wasAlive = false;
var gameStarted = false;

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
        Console.WriteLine($"{logPrefix} Connection closed by server");
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
        if (isAiMode)
        {
            Console.WriteLine($"{logPrefix} Connected! Player ID: {myId}");
        }
        break;
    }
    else
    {
        Console.WriteLine($"{logPrefix} Unexpected package type: {pkg.Type}");
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
        if (isAiMode)
        {
            Console.WriteLine($"{logPrefix} Connection error: {ex.Message}");
        }
        else
        {
            Console.SetCursorPosition(0, 25);
            Console.WriteLine($"Connection error: {ex.Message}");
        }
    }
});

if (isAiMode)
{
    // AI自动移动任务
    var aiTask = Task.Run(async () =>
    {
        var random = new Random();
        var lastMoveTime = DateTime.Now;
        var moveInterval = TimeSpan.FromMilliseconds(100); // 每100ms发送一次移动指令

        while (running)
        {
            await Task.Delay(50);

            ServerState snapshot;
            int idSnapshot;
            lock (stateLock)
            {
                snapshot = latestState;
                idSnapshot = myId;
            }

            // 找到自己的玩家信息
            var myPlayer = snapshot.Players.FirstOrDefault(p => p.Id == idSnapshot);
            if (myPlayer == null || !myPlayer.Alive)
            {
                continue;
            }

            // 如果游戏已开始，定期发送移动指令
            if (gameStarted && DateTime.Now - lastMoveTime >= moveInterval)
            {
                var direction = ChooseDirection(snapshot, myPlayer);
                if (direction != null)
                {
                    await SendDirectionAsync(stream, direction);
                    lastMoveTime = DateTime.Now;
                }
            }
        }
    });

    // 等待任务完成
    await Task.WhenAll(listenTask, aiTask);

    Console.WriteLine($"{logPrefix} Disconnected. Press any key to exit.");
    if (!Console.IsInputRedirected)
    {
        Console.ReadKey(true);
    }
}
else
{
    // Input task (真实玩家模式)
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

    // Render loop (真实玩家模式)
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
}

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
                        if (isAiMode)
                        {
                            ProcessGameState(state);
                        }
                        
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

void ProcessGameState(ServerState state)
{
    var myPlayer = state.Players.FirstOrDefault(p => p.Id == myId);
    
    // 检测游戏开始
    if (!gameStarted && myPlayer != null && myPlayer.Alive)
    {
        gameStarted = true;
        Console.WriteLine($"{logPrefix} 游戏开始 - Player ID: {myId}, Name: {myPlayer.Name}");
        wasAlive = true;
        lastScore = myPlayer.Score;
    }

    if (myPlayer == null) return;

    // 检测吃食（分数增加）
    if (myPlayer.Score > lastScore)
    {
        Console.WriteLine($"{logPrefix} 吃食 - Score: {lastScore} -> {myPlayer.Score}");
        lastScore = myPlayer.Score;
    }

    // 检测死亡
    if (wasAlive && !myPlayer.Alive)
    {
        Console.WriteLine($"{logPrefix} 死亡 - Final Score: {myPlayer.Score}");
        wasAlive = false;
    }

    // 检测游戏结束（所有玩家都死亡）
    if (gameStarted && state.Players.Count > 0 && !state.Players.Any(p => p.Alive))
    {
        Console.WriteLine($"{logPrefix} 游戏结束 - All players dead");
        gameStarted = false;
    }
}

string? ChooseDirection(ServerState state, PlayerView myPlayer)
{
    if (myPlayer.Segments.Count == 0) return null;

    var head = myPlayer.Segments[0];
    var currentDir = myPlayer.Direction;

    // 简单的AI策略：优先朝向食物移动，避免撞墙和自己的身体
    var foods = state.Foods;
    var allSegments = new HashSet<Pos>(myPlayer.Segments);
    
    // 获取所有其他玩家的身体位置
    foreach (var p in state.Players)
    {
        if (p.Id != myId && p.Alive)
        {
            foreach (var seg in p.Segments)
            {
                allSegments.Add(seg);
            }
        }
    }

    // 找到最近的食物
    Pos? nearestFood = null;
    double minDistance = double.MaxValue;
    foreach (var food in foods)
    {
        var dist = Math.Abs(food.X - head.X) + Math.Abs(food.Y - head.Y);
        if (dist < minDistance)
        {
            minDistance = dist;
            nearestFood = food;
        }
    }

    // 尝试朝向食物移动
    if (nearestFood.HasValue)
    {
        var target = nearestFood.Value;
        var candidates = new List<(string dir, Pos pos, int priority)>();

        // 计算各个方向的优先级
        if (target.X > head.X && currentDir != Direction.Left)
        {
            var nextPos = new Pos(head.X + 1, head.Y);
            if (IsSafeMove(nextPos, state, allSegments))
            {
                var priority = target.X - head.X;
                candidates.Add(("Right", nextPos, priority));
            }
        }
        if (target.X < head.X && currentDir != Direction.Right)
        {
            var nextPos = new Pos(head.X - 1, head.Y);
            if (IsSafeMove(nextPos, state, allSegments))
            {
                var priority = head.X - target.X;
                candidates.Add(("Left", nextPos, priority));
            }
        }
        if (target.Y > head.Y && currentDir != Direction.Up)
        {
            var nextPos = new Pos(head.X, head.Y + 1);
            if (IsSafeMove(nextPos, state, allSegments))
            {
                var priority = target.Y - head.Y;
                candidates.Add(("Down", nextPos, priority));
            }
        }
        if (target.Y < head.Y && currentDir != Direction.Down)
        {
            var nextPos = new Pos(head.X, head.Y - 1);
            if (IsSafeMove(nextPos, state, allSegments))
            {
                var priority = head.Y - target.Y;
                candidates.Add(("Up", nextPos, priority));
            }
        }

        // 选择优先级最高的安全方向
        if (candidates.Count > 0)
        {
            var best = candidates.OrderByDescending(c => c.priority).First();
            return best.dir;
        }
    }

    // 如果没有安全的方向朝向食物，尝试保持当前方向或选择任意安全方向
    var safeDirections = new List<string>();
    var directions = new[] { "Up", "Down", "Left", "Right" };
    
    foreach (var dir in directions)
    {
        if (IsOpposite(currentDir, dir)) continue;
        
        var nextPos = dir switch
        {
            "Up" => new Pos(head.X, head.Y - 1),
            "Down" => new Pos(head.X, head.Y + 1),
            "Left" => new Pos(head.X - 1, head.Y),
            "Right" => new Pos(head.X + 1, head.Y),
            _ => head
        };

        if (IsSafeMove(nextPos, state, allSegments))
        {
            safeDirections.Add(dir);
        }
    }

    if (safeDirections.Count > 0)
    {
        // 优先保持当前方向
        var currentDirStr = currentDir.ToString();
        if (safeDirections.Contains(currentDirStr))
        {
            return currentDirStr;
        }
        return safeDirections[Random.Shared.Next(safeDirections.Count)];
    }

    // 如果所有方向都不安全，至少避免反向
    var oppositeDir = GetOppositeDirection(currentDir);
    var allDirs = directions.Where(d => d != oppositeDir).ToList();
    return allDirs.Count > 0 ? allDirs[Random.Shared.Next(allDirs.Count)] : null;
}

bool IsSafeMove(Pos pos, ServerState state, HashSet<Pos> obstacles)
{
    // 检查边界
    if (pos.X < 0 || pos.X >= state.Width || pos.Y < 0 || pos.Y >= state.Height)
        return false;

    // 检查是否撞到障碍物（不包括尾部，因为尾部会移动）
    return !obstacles.Contains(pos);
}

bool IsOpposite(Direction current, string dir)
{
    return (current, dir) switch
    {
        (Direction.Up, "Down") => true,
        (Direction.Down, "Up") => true,
        (Direction.Left, "Right") => true,
        (Direction.Right, "Left") => true,
        _ => false
    };
}

string GetOppositeDirection(Direction dir)
{
    return dir switch
    {
        Direction.Up => "Down",
        Direction.Down => "Up",
        Direction.Left => "Right",
        Direction.Right => "Left",
        _ => "Up"
    };
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
