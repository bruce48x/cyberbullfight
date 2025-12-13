using System.Net.Sockets;
using System.Text.Json;

namespace SnakeGame.Client;

class ReconnectManager
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _playerName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _logPrefix;
    private readonly bool _isAiMode;
    private readonly TimeSpan _reconnectDelay;

    public ReconnectManager(
        string host,
        int port,
        string playerName,
        JsonSerializerOptions jsonOptions,
        string logPrefix,
        bool isAiMode,
        TimeSpan reconnectDelay)
    {
        _host = host;
        _port = port;
        _playerName = playerName;
        _jsonOptions = jsonOptions;
        _logPrefix = logPrefix;
        _isAiMode = isAiMode;
        _reconnectDelay = reconnectDelay;
    }

    public async Task<(TcpClient? tcpClient, NetworkClient? networkClient, bool success)> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"{_logPrefix} Connecting to {_host}:{_port} as {_playerName}...");
                
                var tcp = new TcpClient();
                await tcp.ConnectAsync(_host, _port);
                
                var stream = tcp.GetStream();
                var networkClient = new NetworkClient(stream, _jsonOptions, _logPrefix, _isAiMode);
                
                Console.WriteLine($"{_logPrefix} Connected successfully!");
                return (tcp, networkClient, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{_logPrefix} Failed to connect: {ex.Message}");
                Console.WriteLine($"{_logPrefix} Retrying in {_reconnectDelay.TotalSeconds} seconds...");
                
                try
                {
                    await Task.Delay(_reconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return (null, null, false);
                }
            }
        }
        
        return (null, null, false);
    }

    public async Task<(TcpClient? tcpClient, NetworkClient? networkClient, bool success)> ReconnectAsync(
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{_logPrefix} Connection lost. Attempting to reconnect...");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine($"{_logPrefix} Reconnecting to {_host}:{_port}...");
                
                var tcp = new TcpClient();
                await tcp.ConnectAsync(_host, _port);
                
                var stream = tcp.GetStream();
                var networkClient = new NetworkClient(stream, _jsonOptions, _logPrefix, _isAiMode);
                
                Console.WriteLine($"{_logPrefix} Reconnected successfully!");
                return (tcp, networkClient, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{_logPrefix} Reconnect failed: {ex.Message}");
                Console.WriteLine($"{_logPrefix} Retrying in {_reconnectDelay.TotalSeconds} seconds...");
                
                try
                {
                    await Task.Delay(_reconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return (null, null, false);
                }
            }
        }
        
        return (null, null, false);
    }
}

