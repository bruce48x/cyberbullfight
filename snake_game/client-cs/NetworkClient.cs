using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using SnakeGame.Client.Protocol;
using SnakeGame.Client.Models;

namespace SnakeGame.Client;

class NetworkClient
{
    private readonly NetworkStream _stream;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _logPrefix;
    private readonly bool _isAiMode;
    private GameStateTracker? _gameStateTracker;

    private readonly List<byte> _buffer = new();
    private readonly byte[] _readBuffer = new byte[4096];

    public NetworkClient(NetworkStream stream, JsonSerializerOptions jsonOptions, string logPrefix, bool isAiMode)
    {
        _stream = stream;
        _jsonOptions = jsonOptions;
        _logPrefix = logPrefix;
        _isAiMode = isAiMode;
    }

    public void SetGameStateTracker(GameStateTracker tracker)
    {
        _gameStateTracker = tracker;
    }

    public async Task<bool> PerformHandshakeAsync(string playerName, Action<int, int, int> onHandshakeComplete)
    {
        // Send handshake
        var handshakeData = new Dictionary<string, object?>
        {
            ["name"] = playerName
        };
        var handshakeBody = JsonSerializer.SerializeToUtf8Bytes(handshakeData);
        var handshakePkg = Package.Encode(PackageType.Handshake, handshakeBody);
        await _stream.WriteAsync(handshakePkg, 0, handshakePkg.Length);
        await _stream.FlushAsync();

        // Wait for handshake response
        while (true)
        {
            var bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length);
            if (bytesRead == 0)
            {
                Console.WriteLine($"{_logPrefix} Connection closed by server");
                return false;
            }

            _buffer.AddRange(_readBuffer.Take(bytesRead));

            var pkg = Package.Decode(_buffer.ToArray());
            if (pkg == null) continue;

            if (pkg.Type == PackageType.Handshake)
            {
                var response = JsonSerializer.Deserialize<Dictionary<string, object?>>(pkg.Body, _jsonOptions);
                if (response != null && response.TryGetValue("user", out var userObj))
                {
                    if (userObj is JsonElement user)
                    {
                        int myId = -1;
                        int width = 32;
                        int height = 18;

                        if (user.TryGetProperty("id", out var idElem))
                            myId = idElem.GetInt32();
                        
                        if (user.TryGetProperty("width", out var widthElem))
                            width = widthElem.GetInt32();
                        
                        if (user.TryGetProperty("height", out var heightElem))
                            height = heightElem.GetInt32();

                        // Send handshake ack
                        var ackPkg = Package.Encode(PackageType.HandshakeAck, null);
                        await _stream.WriteAsync(ackPkg, 0, ackPkg.Length);
                        await _stream.FlushAsync();

                        _buffer.Clear();
                        
                        onHandshakeComplete(myId, width, height);
                        
                        if (_isAiMode)
                        {
                            Console.WriteLine($"{_logPrefix} Connected! Player ID: {myId}");
                        }
                        return true;
                    }
                }
            }
            else
            {
                Console.WriteLine($"{_logPrefix} Unexpected package type: {pkg.Type}");
                return false;
            }
        }
    }

    public async Task ListenAsync(CancellationToken cancellationToken, Action<ServerState> onStateUpdate)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await _stream.ReadAsync(_readBuffer, 0, _readBuffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    // 连接断开
                    throw new IOException("Connection closed by server");
                }

                _buffer.AddRange(_readBuffer.Take(bytesRead));

                while (true)
                {
                    var pkg = Package.Decode(_buffer.ToArray());
                    if (pkg == null) break;

                    var pkgSize = 4 + pkg.Length;
                    _buffer.RemoveRange(0, pkgSize);

                    await ProcessPackageAsync(pkg, onStateUpdate);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不抛出异常
            return;
        }
        catch (Exception ex)
        {
            if (_isAiMode)
            {
                Console.WriteLine($"{_logPrefix} Connection error: {ex.Message}");
            }
            else
            {
                Console.SetCursorPosition(0, 25);
                Console.WriteLine($"Connection error: {ex.Message}");
            }
            throw;
        }
    }

    private async Task ProcessPackageAsync(Package pkg, Action<ServerState> onStateUpdate)
    {
        switch (pkg.Type)
        {
            case PackageType.Heartbeat:
                // Send heartbeat response
                var heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
                await _stream.WriteAsync(heartbeatPkg, 0, heartbeatPkg.Length);
                await _stream.FlushAsync();
                break;

            case PackageType.Data:
                var msg = Message.Decode(pkg.Body);
                if (msg == null) return;

                if (msg.Type == MessageType.Push && msg.Route == "snake.state")
                {
                    try
                    {
                        var state = JsonSerializer.Deserialize<ServerState>(msg.Body, _jsonOptions);
                        if (state != null)
                        {
                            if (_isAiMode && _gameStateTracker != null)
                            {
                                _gameStateTracker.ProcessGameState(state);
                            }
                            
                            onStateUpdate(state);
                        }
                    }
                    catch { }
                }
                break;
        }
    }

    public async Task SendDirectionAsync(string direction)
    {
        var body = new Dictionary<string, object?>
        {
            ["dir"] = direction
        };
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body);
        var notifyMsg = Message.Encode(0, MessageType.Notify, false, "snake.move", bodyBytes);
        var dataPkg = Package.Encode(PackageType.Data, notifyMsg);
        await _stream.WriteAsync(dataPkg, 0, dataPkg.Length);
        await _stream.FlushAsync();
    }
}

