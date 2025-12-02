using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text.Json;
using ServerCs.Protocol;
using ServerCs.Socket;

namespace ServerCs.Session;

public enum ConnectionState
{
    Inited,
    WaitAck,
    Working,
    Closed
}

public delegate Dictionary<string, object?> RouteHandler(Session s, Dictionary<string, object?>? body);

public class Session
{
    private static readonly ConcurrentDictionary<string, RouteHandler> Handlers = new();

    public static void RegisterHandler(string route, RouteHandler handler)
    {
        Handlers[route] = handler;
    }

    private readonly IConnection _conn;
    private readonly NetworkStream _stream;
    private ConnectionState _state = ConnectionState.Inited;
    private TimeSpan _heartbeatInterval;
    private TimeSpan _heartbeatTimeout;
    private DateTime _lastHeartbeat;
    private CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    public int ReqId { get; set; }

    public Session(IConnection conn)
    {
        _conn = conn;
        ReqId = 0;
    }

    public async Task StartAsync2()
    {
        var conn = _conn;
        var reader = conn.Input;
        var writer = conn.Output;

        Console.WriteLine($"TCP connected: {conn.RemoteEndPoint}");

        while (!_cts.Token.IsCancellationRequested)
        {
            ReadResult result = await reader.ReadAsync();
            var buffer = result.Buffer;

            if (result.IsCompleted && buffer.Length == 0)
                break;

            while (TryReadOneMessage(ref buffer, out ReadOnlySequence<byte> msg))
            {
                var pkg = Package.Decode(msg.ToArray());
                if (pkg != null)
                {
                    ProcessPackage(pkg);
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                break;
        }

        await reader.CompleteAsync();
        await writer.CompleteAsync();

        Console.WriteLine($"TCP disconnected: {conn.RemoteEndPoint}");
    }

    public bool TryReadOneMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> msg)
    {
        msg = default;

        if (buffer.Length < 4)
            return false;

        // 读取长度字段（前4字节 big-endian）
        Span<byte> lenBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lenBytes);
        int bodyLen = (lenBytes[0] << 24) |
                      (lenBytes[1] << 16) |
                      (lenBytes[2] << 8) |
                      lenBytes[3];

        if (buffer.Length < 4 + bodyLen)
            return false;

        msg = buffer.Slice(4, bodyLen);
        buffer = buffer.Slice(4 + bodyLen);

        return true;
    }

    private void ProcessPackage(Package pkg)
    {
        switch (pkg.Type)
        {
            case PackageType.Handshake:
                HandleHandshake(pkg.Body);
                break;
            case PackageType.HandshakeAck:
                HandleHandshakeAck();
                break;
            case PackageType.Heartbeat:
                HandleHeartbeat();
                break;
            case PackageType.Data:
                HandleData(pkg.Body);
                break;
            case PackageType.Kick:
                Close();
                break;
        }
    }

    private void HandleHandshake(byte[] body)
    {
        var response = new Dictionary<string, object?>
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
            ["user"] = new Dictionary<string, object?>()
        };

        byte[] responseBody = JsonSerializer.SerializeToUtf8Bytes(response);
        byte[] responsePkg = Package.Encode(PackageType.Handshake, responseBody);
        Send(responsePkg);

        lock (_lock)
        {
            _state = ConnectionState.WaitAck;
            _heartbeatInterval = TimeSpan.FromSeconds(10);
            _heartbeatTimeout = TimeSpan.FromSeconds(20);
        }
    }

    private void HandleHandshakeAck()
    {
        lock (_lock)
        {
            _state = ConnectionState.Working;
            _lastHeartbeat = DateTime.UtcNow;
        }

        // Start heartbeat
        _ = HeartbeatLoopAsync();
    }

    private void HandleHeartbeat()
    {
        lock (_lock)
        {
            _lastHeartbeat = DateTime.UtcNow;
        }

        // Send heartbeat response
        byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
        Send(heartbeatPkg);
    }

    private void HandleData(byte[] body)
    {
        lock (_lock)
        {
            _lastHeartbeat = DateTime.UtcNow;
        }

        var msg = Message.Decode(body);
        if (msg == null)
        {
            Console.WriteLine("[session] Failed to decode message");
            return;
        }

        Dictionary<string, object?>? msgBody = null;
        if (msg.Body.Length > 0)
        {
            try
            {
                msgBody = JsonSerializer.Deserialize<Dictionary<string, object?>>(msg.Body);
            }
            catch { }
        }

        if (msg.Type == MessageType.Request)
        {
            HandleRequest(msg.Id, msg.Route, msgBody);
        }
        else if (msg.Type == MessageType.Notify)
        {
            Console.WriteLine($"[session] Notify received: route={msg.Route}, body={JsonSerializer.Serialize(msgBody)}");
        }
    }

    private void HandleRequest(int id, string route, Dictionary<string, object?>? body)
    {
        Dictionary<string, object?> responseBody;

        if (Handlers.TryGetValue(route, out var handler))
        {
            responseBody = handler(this, body);
        }
        else
        {
            Console.WriteLine($"[session] Unknown route: {route}");
            responseBody = new Dictionary<string, object?>
            {
                ["code"] = 404,
                ["msg"] = $"Route not found: {route}"
            };
        }

        byte[] responseBodyBytes = JsonSerializer.SerializeToUtf8Bytes(responseBody);
        byte[] responseMsg = Message.Encode(id, MessageType.Response, false, "", responseBodyBytes);
        byte[] responsePkg = Package.Encode(PackageType.Data, responseMsg);
        Send(responsePkg);
    }

    private async Task HeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(_heartbeatInterval);

        while (await timer.WaitForNextTickAsync(_cts.Token))
        {
            ConnectionState state;
            DateTime lastHB;

            lock (_lock)
            {
                state = _state;
                lastHB = _lastHeartbeat;
            }

            if (state != ConnectionState.Working)
                return;

            // Check timeout
            if (DateTime.UtcNow - lastHB > _heartbeatTimeout)
            {
                Console.WriteLine("[session] Heartbeat timeout");
                Close();
                return;
            }

            // Send heartbeat
            byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
            Send(heartbeatPkg);
        }
    }

    private void Send(byte[] data)
    {
        try
        {
            _stream.Write(data);
        }
        catch { }
    }

    public void Close()
    {
        lock (_lock)
        {
            if (_state == ConnectionState.Closed)
                return;
            _state = ConnectionState.Closed;
        }

        _cts.Cancel();
        _stream.Close();
        Console.WriteLine("[session] Connection closed");
    }
}

