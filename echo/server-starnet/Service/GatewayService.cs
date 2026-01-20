using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServerCs.Protocol;
using ServerCs.Actor.Message;

namespace ServerCs.Actor;

public delegate string RouteHandler(Session session, JsonElement body);

public class GatewayService : Service
{
    private static readonly ConcurrentDictionary<string, RouteHandler> Handlers = new();
    private static readonly object HandlersLock = new();

    private readonly ConcurrentDictionary<int, Session> _sessions = new();
    private readonly object _sessionsLock = new();

    private volatile bool _heartbeatRunning = true;
    private Thread? _heartbeatThread;
    
    public uint Port { get; set; } = 3010; // Default port

    public GatewayService()
    {
        _heartbeatThread = new Thread(() =>
        {
            while (_heartbeatRunning)
            {
                Thread.Sleep(1000);
                CheckHeartbeatTimeout();
            }
        });
        _heartbeatThread.Start();
    }

    public static void RegisterHandler(string route, RouteHandler handler)
    {
        lock (HandlersLock)
        {
            Handlers[route] = handler;
        }
    }

    public override void OnInit()
    {
        Console.WriteLine($"[GatewayService] OnInit id={Id}");
        
        // Start listening on port - this is the actor's responsibility
        int listenFd = Starnet.Instance.Listen(Port, Id);
        if (listenFd < 0)
        {
            Console.WriteLine($"[GatewayService] Failed to listen on port {Port}");
            return;
        }
        
        Console.WriteLine($"[GatewayService] Server listening on port {Port}");
    }

    public override void OnMsg(BaseMsg msg)
    {
        if (msg.Type == MsgType.SocketAccept)
        {
            if (msg is SocketAcceptMsg acceptMsg)
            {
                OnAcceptMsg(acceptMsg);
            }
        }
        else if (msg.Type == MsgType.SocketRW)
        {
            if (msg is SocketRWMsg rwMsg)
            {
                OnRWMsg(rwMsg);
            }
        }
    }

    private bool TryReadPackage(ref ReadOnlySequence<byte> buffer, out Package? pkg)
    {
        pkg = null;

        if (buffer.Length < 4)
            return false;

        Span<byte> lenBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lenBytes);
        byte type = lenBytes[0];
        int bodyLen = (lenBytes[1] << 16) |
                      (lenBytes[2] << 8) |
                      lenBytes[3];

        if (buffer.Length < 4 + bodyLen)
            return false;

        pkg = new Package
        {
            Type = type,
            Length = bodyLen,
            Body = buffer.Slice(4, bodyLen).ToArray()
        };
        buffer = buffer.Slice(4 + bodyLen);

        return true;
    }

    public override void OnExit()
    {
        Console.WriteLine($"[GatewayService] OnExit id={Id}");
        _heartbeatRunning = false;
        _heartbeatThread?.Join();

        lock (_sessionsLock)
        {
            foreach (var pair in _sessions)
            {
                // Complete pipes
                try
                {
                    pair.Value.Pipe.Writer.Complete();
                    pair.Value.Pipe.Reader.Complete();
                }
                catch { }
                
                Starnet.Instance.CloseConn(pair.Key);
            }
            _sessions.Clear();
        }
    }

    protected override void OnAcceptMsg(SocketAcceptMsg msg)
    {
        int clientFd = msg.ClientFd;
        Console.WriteLine($"[GatewayService] OnAcceptMsg clientFd={clientFd}");

        var socket = Starnet.Instance.GetSocket(clientFd);
        if (socket != null)
        {
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }

        // Create session with Pipe
        var session = new Session
        {
            Fd = clientFd,
            State = ConnectionState.Inited,
            LastHeartbeat = DateTime.UtcNow,
            Pipe = new Pipe()
        };
        _sessions[clientFd] = session;

        // Start processing packets from Pipe in background
        _ = Task.Run(async () => await ProcessPacketsAsync(clientFd, session.Pipe.Reader));
        
        // Immediately check for incoming data (handshake packet)
        // Client may have already sent handshake before we detect socket readable
        if (socket != null && socket.Available > 0)
        {
            OnSocketRead(clientFd, socket);
        }
    }

    protected override void OnRWMsg(SocketRWMsg msg)
    {
        int fd = msg.Fd;
        var socket = Starnet.Instance.GetSocket(fd);
        if (socket == null) return;

        if (msg.IsRead)
        {
            OnSocketRead(fd, socket);
        }

        if (msg.IsWrite)
        {
            OnSocketWrite(fd, socket);
        }
    }

    public void OnSocketRead(int fd, System.Net.Sockets.Socket socket)
    {
        if (!_sessions.TryGetValue(fd, out var session))
        {
            return;
        }

        const int minimumBufferSize = 1024;
        var writer = session.Pipe.Writer;

        try
        {
            // Read available data in non-blocking way
            while (true)
            {
                var memory = writer.GetMemory(minimumBufferSize);
                int bytesRead;
                
                try
                {
                    bytesRead = socket.Receive(memory.Span, SocketFlags.None);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.WouldBlock ||
                        ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // No more data available
                        break;
                    }
                    else
                    {
                        // Error occurred
                        writer.Complete(ex);
                        OnSocketClose(fd);
                        Starnet.Instance.CloseConn(fd);
                        return;
                    }
                }
                
                if (bytesRead == 0)
                {
                    // Connection closed
                    writer.Complete();
                    OnSocketClose(fd);
                    Starnet.Instance.CloseConn(fd);
                    return;
                }

                // Tell the PipeWriter how much was read
                writer.Advance(bytesRead);
            }
            
            // Flush all data to make it available to reader
            // Do this after reading loop to avoid blocking in the loop
            try
            {
                var flushResult = writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                if (flushResult.IsCompleted)
                {
                    // Pipe is completed, stop reading
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GatewayService] Flush error fd={fd}: {ex.Message}");
                writer.Complete(ex);
                OnSocketClose(fd);
                Starnet.Instance.CloseConn(fd);
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GatewayService] OnSocketRead error fd={fd}: {ex.Message}");
            writer.Complete(ex);
            OnSocketClose(fd);
            Starnet.Instance.CloseConn(fd);
        }
    }

    public void OnSocketWrite(int fd, System.Net.Sockets.Socket socket)
    {
        // Handle write buffer if needed in the future
        // For now, we send directly in SendAsync
    }

    private async Task ProcessPacketsAsync(int fd, PipeReader reader)
    {
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                var buffer = result.Buffer;

                if (result.IsCompleted && buffer.Length == 0)
                {
                    break;
                }

                // Process complete packages
                while (TryReadPackage(ref buffer, out var package))
                {
                    ProcessPackage(fd, package);
                }

                // Tell the PipeReader how much of the buffer we consumed
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GatewayService] ProcessPacketsAsync error fd={fd}: {ex.Message}");
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }


    protected override void OnSocketClose(int fd)
    {
        Console.WriteLine($"[GatewayService] OnSocketClose fd={fd}");
        _sessions.TryRemove(fd, out _);
    }

    private void ProcessPackage(int fd, Package pkg)
    {
        switch (pkg.Type)
        {
            case PackageType.Handshake:
                _ = HandleHandshakeAsync(fd, pkg.Body);
                break;
            case PackageType.HandshakeAck:
                HandleHandshakeAck(fd);
                break;
            case PackageType.Heartbeat:
                _ = HandleHeartbeatAsync(fd);
                break;
            case PackageType.Data:
                HandleData(fd, pkg.Body);
                break;
            case PackageType.Kick:
                OnSocketClose(fd);
                Starnet.Instance.CloseConn(fd);
                break;
            default:
                Console.WriteLine($"[GatewayService] Unknown package type: {pkg.Type}");
                break;
        }
    }

    private async Task HandleHandshakeAsync(int fd, byte[] body)
    {
        Console.WriteLine($"[GatewayService] handle_handshake fd={fd}");
        string response = "{\"code\":200,\"sys\":{\"heartbeat\":10,\"dict\":{},\"protos\":{\"client\":{},\"server\":{}}},\"user\":{}}";
        byte[] responseBody = Encoding.UTF8.GetBytes(response);
        byte[] responsePkg = Package.Encode(PackageType.Handshake, responseBody);
        Console.WriteLine($"[GatewayService] Sending handshake response, size={responsePkg.Length}");
        await SendAsync(fd, responsePkg);

        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(fd, out var session))
            {
                session.State = ConnectionState.WaitAck;
                session.HeartbeatInterval = TimeSpan.FromSeconds(10);
                session.HeartbeatTimeout = TimeSpan.FromSeconds(20);
                Console.WriteLine("[GatewayService] Handshake response sent, state=WaitAck");
            }
        }
    }

    private void HandleHandshakeAck(int fd)
    {
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(fd, out var session))
            {
                session.State = ConnectionState.Working;
                session.LastHeartbeat = DateTime.UtcNow;
            }
        }
    }

    private async Task HandleHeartbeatAsync(int fd)
    {
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(fd, out var session))
            {
                session.LastHeartbeat = DateTime.UtcNow;
            }
        }

        byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
        await SendAsync(fd, heartbeatPkg);
    }

    private void HandleData(int fd, byte[] body)
    {
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(fd, out var session))
            {
                session.LastHeartbeat = DateTime.UtcNow;
            }
        }

        var msg = Protocol.Message.Decode(body);
        if (msg == null)
        {
            Console.WriteLine($"[GatewayService] Failed to decode message, body_size={body.Length}");
            return;
        }

        string msgBody = msg.Body.Length > 0 ? Encoding.UTF8.GetString(msg.Body) : "{}";
        if (msg.Type == MessageType.Request)
        {
            _ = HandleRequestAsync(fd, msg.Id, msg.Route, msgBody);
        }
        else if (msg.Type == MessageType.Notify)
        {
            Console.WriteLine($"[GatewayService] Notify received: route={msg.Route}, body={msgBody}");
        }
        else
        {
            Console.WriteLine($"[GatewayService] Unknown message type: {msg.Type}");
        }
    }

    private async Task HandleRequestAsync(int fd, int id, string route, string body)
    {
        Console.WriteLine($"[GatewayService] handle_request fd={fd}, id={id}, route={route}, body={body}");
        string responseBody;
        RouteHandler? handler = null;
        Session? session = null;

        // Get handler and session outside of async operations
        lock (HandlersLock)
        {
            Handlers.TryGetValue(route, out handler);
        }

        lock (_sessionsLock)
        {
            if (!_sessions.TryGetValue(fd, out session))
            {
                Console.WriteLine($"[GatewayService] Session not found for fd={fd}");
                return;
            }
        }

        if (handler != null)
        {
            JsonElement bodyJson;
            try
            {
                if (!string.IsNullOrEmpty(body))
                {
                    bodyJson = JsonDocument.Parse(body).RootElement;
                }
                else
                {
                    bodyJson = JsonDocument.Parse("{}").RootElement;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[GatewayService] Failed to parse JSON body: {ex.Message}");
                string errorResponseBody = "{\"code\":400,\"msg\":\"Invalid JSON\"}";
                byte[] errorResponseBytes = Encoding.UTF8.GetBytes(errorResponseBody);
                byte[] errorResponseMsg = Protocol.Message.Encode(id, MessageType.Response, false, "", errorResponseBytes);
                byte[] errorResponsePkg = Package.Encode(PackageType.Data, errorResponseMsg);
                Console.WriteLine($"[GatewayService] Sending error response, pkg_size={errorResponsePkg.Length}");
                await SendAsync(fd, errorResponsePkg);
                return;
            }

            responseBody = handler(session, bodyJson);
        }
        else
        {
            Console.WriteLine($"[GatewayService] Unknown route: {route}");
            responseBody = $"{{\"code\":404,\"msg\":\"Route not found: {route}\"}}";
        }

        byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
        byte[] responseMsg = Protocol.Message.Encode(id, MessageType.Response, false, "", responseBytes);
        byte[] responsePkg = Package.Encode(PackageType.Data, responseMsg);
        await SendAsync(fd, responsePkg);
    }

    private async Task SendAsync(int fd, byte[] data)
    {
        try
        {
            var socket = Starnet.Instance.GetSocket(fd);
            if (socket == null) return;

            int sent = 0;
            while (sent < data.Length)
            {
                int n = await socket.SendAsync(new ArraySegment<byte>(data, sent, data.Length - sent), SocketFlags.None);
                if (n <= 0)
                {
                    Console.WriteLine($"[GatewayService] Send returned 0, fd={fd}");
                    OnSocketClose(fd);
                    Starnet.Instance.CloseConn(fd);
                    return;
                }
                sent += n;
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[GatewayService] Send error: {ex.SocketErrorCode}, fd={fd}");
            OnSocketClose(fd);
            Starnet.Instance.CloseConn(fd);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GatewayService] Send error: {ex.Message}, fd={fd}");
            OnSocketClose(fd);
            Starnet.Instance.CloseConn(fd);
        }
    }

    private void Send(int fd, byte[] data)
    {
        // Fire and forget - send asynchronously
        _ = SendAsync(fd, data);
    }

    private async Task SendHeartbeatAsync(int fd)
    {
        byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
        await SendAsync(fd, heartbeatPkg);
    }

    private void CheckHeartbeatTimeout()
    {
        var now = DateTime.UtcNow;
        var timeoutFds = new List<int>();

        lock (_sessionsLock)
        {
            foreach (var pair in _sessions)
            {
                var session = pair.Value;
                if (session.State == ConnectionState.Working)
                {
                    var elapsed = now - session.LastHeartbeat;
                    if (elapsed > session.HeartbeatTimeout)
                    {
                        timeoutFds.Add(pair.Key);
                    }
                    else if (elapsed >= session.HeartbeatInterval)
                    {
                        _ = SendHeartbeatAsync(pair.Key);
                    }
                }
            }
        }

        foreach (int fd in timeoutFds)
        {
            Console.WriteLine($"[GatewayService] Heartbeat timeout fd={fd}");
            OnSocketClose(fd);
            Starnet.Instance.CloseConn(fd);
        }
    }
}
