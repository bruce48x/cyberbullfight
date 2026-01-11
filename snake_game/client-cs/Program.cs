using System.Net.Sockets;
using System.Text.Json;
using SnakeGame.Client.Protocol;
using SnakeGame.Client.Models;
using SnakeGame.Client;

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
var reconnectDelay = TimeSpan.FromSeconds(3); // 重连延迟3秒

// 创建重连管理器
var reconnectManager = new ReconnectManager(host, port, playerName, jsonOptions, logPrefix, isAiMode, reconnectDelay);

var latestState = new ServerState();
var myId = -1;
var stateLock = new object();
var globalCts = new CancellationTokenSource();

// 初始连接
TcpClient? tcp = null;
NetworkClient? networkClient = null;
GameStateTracker? gameStateTracker = null;
AIController? aiController = null;
Renderer? renderer = !isAiMode ? new Renderer(playerName) : null;

// 连接和握手循环
while (!globalCts.Token.IsCancellationRequested)
{
    // 尝试连接
    var (newTcp, newNetworkClient, connectSuccess) = await reconnectManager.ConnectAsync(globalCts.Token);
    if (!connectSuccess)
    {
        break; // 用户取消
    }

    tcp = newTcp;
    networkClient = newNetworkClient;

    if (networkClient == null || tcp == null)
    {
        break; // 连接失败
    }

    // 执行握手
    var handshakeSuccess = await networkClient.PerformHandshakeAsync(playerName, (id, width, height) =>
    {
        myId = id;
        latestState.Width = width;
        latestState.Height = height;
        
        // 初始化游戏状态跟踪器（AI模式使用）
        if (isAiMode)
        {
            gameStateTracker = new GameStateTracker(myId, logPrefix);
            networkClient.SetGameStateTracker(gameStateTracker);
        }
    });

    if (!handshakeSuccess)
    {
        // 握手失败，清理并重连
        try
        {
            tcp?.Close();
            tcp?.Dispose();
        }
        catch { }
        continue;
    }

    // 创建AI控制器
    if (isAiMode && aiController == null)
    {
        aiController = new AIController(myId);
    }

    // 启动游戏循环
    try
    {
        if (isAiMode)
        {
            await RunAiModeAsync(networkClient, tcp, gameStateTracker, aiController, latestState, myId, stateLock, globalCts.Token);
        }
        else
        {
            await RunPlayerModeAsync(networkClient, tcp, renderer, latestState, myId, stateLock, globalCts.Token);
        }
    }
    catch (Exception ex)
    {
        if (globalCts.Token.IsCancellationRequested)
        {
            break; // 用户取消，退出
        }

        // 连接断开，尝试重连
        Console.WriteLine($"{logPrefix} Connection lost: {ex.Message}");
        
        try
        {
            tcp?.Close();
            tcp?.Dispose();
        }
        catch { }

        // 重连
        var (reconnectTcp, reconnectNetworkClient, reconnectSuccess) = await reconnectManager.ReconnectAsync(globalCts.Token);
        if (!reconnectSuccess)
        {
            break; // 重连失败且用户取消
        }

        tcp = reconnectTcp;
        networkClient = reconnectNetworkClient;

        if (networkClient == null || tcp == null)
        {
            continue; // 重连失败，继续重连循环
        }

        // 重新握手
        handshakeSuccess = await networkClient.PerformHandshakeAsync(playerName, (id, width, height) =>
        {
            myId = id;
            latestState.Width = width;
            latestState.Height = height;
            
            if (isAiMode)
            {
                gameStateTracker = new GameStateTracker(myId, logPrefix);
                networkClient.SetGameStateTracker(gameStateTracker);
            }
        });

        if (!handshakeSuccess)
        {
            continue; // 握手失败，继续重连循环
        }

        // 继续游戏循环
        continue;
    }
}

// 清理
try
{
    tcp?.Close();
    tcp?.Dispose();
}
catch { }

Console.WriteLine($"{logPrefix} Disconnected. Press any key to exit.");
if (!Console.IsInputRedirected)
{
    Console.ReadKey(true);
}

async Task RunAiModeAsync(
    NetworkClient networkClient,
    TcpClient tcp,
    GameStateTracker? gameStateTracker,
    AIController? aiController,
    ServerState latestState,
    int myId,
    object stateLock,
    CancellationToken cancellationToken)
{
    var running = true;
    var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // 启动监听任务
    var listenTask = Task.Run(async () =>
    {
        try
        {
            await networkClient.ListenAsync(listenCts.Token, (state) =>
            {
                lock (stateLock)
                {
                    latestState = state;
                }
            });
        }
        catch
        {
            running = false;
            throw; // 重新抛出异常以触发重连
        }
    });

    // AI自动移动任务
    var aiTask = Task.Run(async () =>
    {
        var lastMoveTime = DateTime.Now;
        var moveInterval = TimeSpan.FromMilliseconds(100); // 每100ms发送一次移动指令

        while (running && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(50, cancellationToken);

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
            if (gameStateTracker != null && gameStateTracker.IsGameStarted && DateTime.Now - lastMoveTime >= moveInterval)
            {
                try
                {
                    var direction = aiController?.ChooseDirection(snapshot, myPlayer);
                    if (direction != null)
                    {
                        await networkClient.SendDirectionAsync(direction);
                        lastMoveTime = DateTime.Now;
                    }
                }
                catch
                {
                    // 发送失败，可能连接已断开
                    running = false;
                    listenCts.Cancel();
                    throw;
                }
            }
        }
    });

    // 等待任务完成
    await Task.WhenAny(listenTask, aiTask);
    
    // 取消另一个任务
    listenCts.Cancel();
    running = false;
    
    // 等待所有任务完成
    try
    {
        await Task.WhenAll(listenTask, aiTask);
    }
    catch
    {
        // 忽略异常，因为我们已经知道连接断开了
    }
}

async Task RunPlayerModeAsync(
    NetworkClient networkClient,
    TcpClient tcp,
    Renderer? renderer,
    ServerState latestState,
    int myId,
    object stateLock,
    CancellationToken cancellationToken)
{
    var running = true;
    var listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // 启动监听任务
    var listenTask = Task.Run(async () =>
    {
        try
        {
            await networkClient.ListenAsync(listenCts.Token, (state) =>
            {
                lock (stateLock)
                {
                    latestState = state;
                }
            });
        }
        catch
        {
            running = false;
            throw; // 重新抛出异常以触发重连
        }
    });

    // Input task (真实玩家模式)
    var inputTask = Task.Run(async () =>
    {
        while (running && !cancellationToken.IsCancellationRequested)
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
                    try
                    {
                        await networkClient.SendDirectionAsync(dir);
                    }
                    catch
                    {
                        // 发送失败，可能连接已断开
                        running = false;
                        listenCts.Cancel();
                        throw;
                    }
                }
            }
            await Task.Delay(10, cancellationToken);
        }
    });

    // Render loop (真实玩家模式)
    while (running && !cancellationToken.IsCancellationRequested)
    {
        ServerState snapshot;
        int idSnapshot;
        lock (stateLock)
        {
            snapshot = latestState;
            idSnapshot = myId;
        }

        renderer?.Draw(snapshot, idSnapshot);
        await Task.Delay(80, cancellationToken);
    }

    // 取消监听任务
    listenCts.Cancel();
    
    // 等待所有任务完成
    try
    {
        await Task.WhenAll(listenTask, inputTask);
    }
    catch
    {
        // 忽略异常，因为我们已经知道连接断开了
    }
}
