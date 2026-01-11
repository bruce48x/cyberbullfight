using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Linq;
using System.IO.Pipelines;
using System.Buffers;
using SnakeGame.Server.Protocol;
using SnakeGame.Server;

// Snake server with matchmaking and room system.

var width = 32;
var height = 18;
var tick = TimeSpan.FromMilliseconds(160);
var matchSize = 2; // 默认匹配人数
var listener = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
listener.Bind(new IPEndPoint(IPAddress.Any, 5000));
listener.Listen(128);

Console.WriteLine($"Snake server listening on 0.0.0.0:5000 (Match size: {matchSize})");

var playersLock = new object();
var roomsLock = new object();
var allPlayers = new Dictionary<int, Player>(); // 所有连接的玩家
var rooms = new Dictionary<int, Room>(); // 所有房间
var matchQueue = new MatchQueue(matchSize);
var nextPlayerId = 1;
var nextRoomId = 1;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

var cts = new CancellationTokenSource();

// 启动匹配处理循环
_ = Task.Run(() => MatchLoop(cts.Token));

// 启动房间清理循环
_ = Task.Run(() => RoomCleanupLoop(cts.Token));

while (true)
{
    var socket = await listener.AcceptAsync();
    _ = Task.Run(() => HandleClient(socket));
}

// --- Local functions ---

// 接收循环：使用 Socket.ReceiveAsync 直接将数据写入 Pipe，减少数据复制
async Task ReceiveLoop(System.Net.Sockets.Socket socket, PipeWriter writer, CancellationToken cancellationToken)
{
    const int minimumBufferSize = 1024;
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var memory = writer.GetMemory(minimumBufferSize);
            var bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
            if (bytesRead == 0) break; // 连接关闭

            writer.Advance(bytesRead);

            var result = await writer.FlushAsync(cancellationToken);
            if (result.IsCompleted) break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Receive loop error: {ex}");
    }
    finally
    {
        await writer.CompleteAsync();
    }
}

// 发送数据：使用 Socket.SendAsync 直接发送，减少数据复制
async Task SendDataAsync(System.Net.Sockets.Socket socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
{
    try
    {
        int totalSent = 0;
        while (totalSent < data.Length)
        {
            var remaining = data.Slice(totalSent);
            var sent = await socket.SendAsync(remaining, SocketFlags.None, cancellationToken);
            if (sent == 0) break;
            totalSent += sent;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Send error: {ex}");
        throw;
    }
}

// 匹配循环：定期检查匹配队列，当人数达到要求时创建房间
async Task MatchLoop(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var matchedPlayers = matchQueue.TryMatch();
            if (matchedPlayers != null && matchedPlayers.Count > 0)
            {
                // 创建新房间
                int roomId;
                Room room;
                lock (roomsLock)
                {
                    roomId = nextRoomId++;
                    room = new Room(roomId, width, height, tick);
                    rooms[roomId] = room;
                }

                // 将玩家添加到房间
                foreach (var player in matchedPlayers)
                {
                    lock (playersLock)
                    {
                        if (allPlayers.ContainsKey(player.Id))
                        {
                            player.Status = PlayerStatus.InGame;
                            room.AddPlayer(player);
                            Console.WriteLine($"Player {player.Id} ({player.Name}) joined room {roomId}");
                        }
                    }
                }

                // 启动房间游戏
                await room.StartGameAsync();
                _ = Task.Run(() => room.GameLoopAsync(cancellationToken));
                
                Console.WriteLine($"Room {roomId} started with {matchedPlayers.Count} players");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Match loop error: {ex}");
        }

        await Task.Delay(100, cancellationToken); // 每100ms检查一次匹配
    }
}

// 房间清理循环：检查房间是否应该关闭，游戏结束后将玩家重新加入匹配队列
async Task RoomCleanupLoop(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            List<int> roomsToClose = new();
            List<Player> playersToRematch = new();

            lock (roomsLock)
            {
                foreach (var kvp in rooms)
                {
                    var room = kvp.Value;
                    if (room.CanClose())
                    {
                        // 获取房间内的玩家
                        var playerIds = room.GetPlayerIds();
                        lock (playersLock)
                        {
                            foreach (var playerId in playerIds)
                            {
                                if (allPlayers.TryGetValue(playerId, out var player))
                                {
                                    playersToRematch.Add(player);
                                    player.RoomId = null;
                                    player.Status = PlayerStatus.Matching;
                                    player.Alive = true; // 重置状态
                                }
                            }
                        }
                        roomsToClose.Add(kvp.Key);
                    }
                }
            }

            // 关闭房间
            foreach (var roomId in roomsToClose)
            {
                lock (roomsLock)
                {
                    rooms.Remove(roomId);
                    Console.WriteLine($"Room {roomId} closed");
                }
            }

            // 将玩家重新加入匹配队列
            foreach (var player in playersToRematch)
            {
                matchQueue.Enqueue(player);
                Console.WriteLine($"Player {player.Id} ({player.Name}) returned to match queue");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Room cleanup loop error: {ex}");
        }

        await Task.Delay(200, cancellationToken); // 每200ms检查一次，更快响应游戏结束
    }
}

async Task HandleClient(System.Net.Sockets.Socket socket)
{
    var pipe = new Pipe();
    var reader = pipe.Reader;
    var receivingTask = Task.Run(() => ReceiveLoop(socket, pipe.Writer, cts.Token));
    
    Player? player = null;

    try
    {
        // Wait for handshake
        while (true)
        {
            var result = await reader.ReadAsync(cts.Token);
            var buffer = result.Buffer;

            // Try to decode package
            var tempBuffer = buffer;
            var pkg = Package.Decode(ref tempBuffer);
            if (pkg == null)
            {
                // Not enough data, continue reading
                if (buffer.Length > 1024 * 10) // Prevent buffer overflow
                {
                    Console.WriteLine("Buffer overflow");
                    return;
                }
                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            // Process handshake
            if (pkg.Type == PackageType.Handshake)
            {
                var handshakeData = JsonSerializer.Deserialize<Dictionary<string, object?>>(pkg.Body, jsonOptions);
                var playerName = handshakeData?.TryGetValue("name", out var name) == true 
                    ? name?.ToString() 
                    : null;

                lock (playersLock)
                {
                    var id = nextPlayerId++;
                    player = new Player
                    {
                        Id = id,
                        Name = playerName ?? $"Player{id}",
                        Socket = socket,
                        Status = PlayerStatus.Matching,
                        RoomId = null
                    };
                    allPlayers[id] = player;
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
                await SendDataAsync(socket, responsePkg, cts.Token);

                var consumed = buffer.GetPosition(4 + pkg.Length);
                reader.AdvanceTo(consumed);
                Console.WriteLine($"Player {player.Id} ({player.Name}) connected, joining match queue");

                // 将玩家加入匹配队列
                matchQueue.Enqueue(player);
                break;
            }
            else
            {
                Console.WriteLine($"Unexpected package type during handshake: {pkg.Type}");
                return;
            }
        }

        // Wait for handshake ack
        while (true)
        {
            var result = await reader.ReadAsync(cts.Token);
            var buffer = result.Buffer;

            var tempBuffer = buffer;
            var pkg = Package.Decode(ref tempBuffer);
            if (pkg == null)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                continue;
            }

            if (pkg.Type == PackageType.HandshakeAck)
            {
                var consumed = buffer.GetPosition(4 + pkg.Length);
                reader.AdvanceTo(consumed);
                break;
            }
            else
            {
                Console.WriteLine($"Unexpected package type: {pkg.Type}");
                return;
            }
        }

        // Main message loop
        while (true)
        {
            var result = await reader.ReadAsync(cts.Token);
            var buffer = result.Buffer;

            var consumed = buffer.Start;
            while (true)
            {
                var tempBuffer = buffer.Slice(consumed);
                var pkg = Package.Decode(ref tempBuffer);
                if (pkg == null) break;

                await ProcessPackageAsync(pkg, player!);
                consumed = buffer.GetPosition(4 + pkg.Length, consumed);
            }

            reader.AdvanceTo(consumed, buffer.End);
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
        try
        {
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }
        catch { }

        await pipe.Writer.CompleteAsync();
        await reader.CompleteAsync();
        await receivingTask;

        if (player is not null)
        {
            lock (playersLock)
            {
                allPlayers.Remove(player.Id);
            }

            // 如果玩家在房间中，从房间移除
            if (player.RoomId.HasValue)
            {
                lock (roomsLock)
                {
                    if (rooms.TryGetValue(player.RoomId.Value, out var room))
                    {
                        room.RemovePlayer(player.Id);
                    }
                }
            }
            else
            {
                // 如果玩家在匹配队列中，从队列移除
                matchQueue.Remove(player);
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
            if (player.Socket != null)
            {
                await SendDataAsync(player.Socket, heartbeatPkg, cts.Token);
            }
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
                        // 如果玩家在房间中，将移动指令传递给房间
                        if (player.RoomId.HasValue)
                        {
                            lock (roomsLock)
                            {
                                if (rooms.TryGetValue(player.RoomId.Value, out var room))
                                {
                                    room.HandlePlayerMove(player.Id, dir);
                                }
                            }
                        }
                    }
                }
            }
            break;
    }
}
