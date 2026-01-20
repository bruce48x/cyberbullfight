using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServerCs.Protocol;

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

    public override void OnExit()
    {
        Console.WriteLine($"[GatewayService] OnExit id={Id}");
        _heartbeatRunning = false;
        _heartbeatThread?.Join();

        lock (_sessionsLock)
        {
            foreach (var pair in _sessions)
            {
                Starnet.Instance.CloseConn(pair.Key);
            }
            _sessions.Clear();
        }
    }

    protected override void OnAcceptMsg(SocketAcceptMsg msg)
    {
        int clientFd = msg.ClientFd;
        Console.WriteLine($"[GatewayService] OnAcceptMsg clientFd={clientFd}");

        var session = new Session
        {
            Fd = clientFd,
            State = ConnectionState.Inited,
            LastHeartbeat = DateTime.UtcNow
        };

        _sessions[clientFd] = session;
    }

    protected override void OnRWMsg(SocketRWMsg msg)
    {
        int fd = msg.Fd;
        if (msg.IsRead)
        {
            const int BUFFSIZE = 4096;
            byte[] buff = new byte[BUFFSIZE];
            int len = 0;

            try
            {
                var socket = Starnet.Instance.GetSocket(fd);
                if (socket == null) return;

                do
                {
                    len = socket.Receive(buff, 0, BUFFSIZE, SocketFlags.None);
                    if (len > 0)
                    {
                        OnSocketData(fd, buff, len);
                    }
                } while (len == BUFFSIZE && socket.Available > 0);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock &&
                    ex.SocketErrorCode != SocketError.TimedOut)
                {
                    if (_sessions.ContainsKey(fd))
                    {
                        OnSocketClose(fd);
                        Starnet.Instance.CloseConn(fd);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GatewayService] Read error: {ex.Message}");
                if (_sessions.ContainsKey(fd))
                {
                    OnSocketClose(fd);
                    Starnet.Instance.CloseConn(fd);
                }
            }
        }

        if (msg.IsWrite)
        {
            if (_sessions.ContainsKey(fd))
            {
                OnSocketWritable(fd);
            }
        }
    }

    protected override void OnSocketData(int fd, byte[] buff, int len)
    {
        Session? session;
        lock (_sessionsLock)
        {
            if (!_sessions.TryGetValue(fd, out session))
            {
                Console.WriteLine($"[GatewayService] Session not found for fd={fd}");
                return;
            }
            session.DataBuf.AddRange(buff.Take(len));
        }

        // Process complete packages
        while (true)
        {
            List<byte>? pkgData = null;
            bool hasPackage = false;

            lock (_sessionsLock)
            {
                if (!_sessions.TryGetValue(fd, out session))
                {
                    return;
                }

                if (session.DataBuf.Count >= 4)
                {
                    int pkgType = session.DataBuf[0];
                    int pkgLen = (session.DataBuf[1] << 16) | (session.DataBuf[2] << 8) | session.DataBuf[3];
                    int totalLen = 4 + pkgLen;

                    if (session.DataBuf.Count >= totalLen)
                    {
                        pkgData = session.DataBuf.Take(totalLen).ToList();
                        session.DataBuf.RemoveRange(0, totalLen);
                        hasPackage = true;
                    }
                }
            }

            if (!hasPackage)
            {
                break;
            }

            if (pkgData != null)
            {
                var pkg = Package.Decode(pkgData.ToArray());
                if (pkg != null)
                {
                    ProcessPackage(fd, pkg);
                }
                else
                {
                    Console.WriteLine("[GatewayService] Failed to decode package");
                }
            }
        }
    }

    protected override void OnSocketWritable(int fd)
    {
        // Handle write buffer if needed
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
                HandleHandshake(fd, pkg.Body);
                break;
            case PackageType.HandshakeAck:
                HandleHandshakeAck(fd);
                break;
            case PackageType.Heartbeat:
                HandleHeartbeat(fd);
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

    private void HandleHandshake(int fd, byte[] body)
    {
        Console.WriteLine($"[GatewayService] handle_handshake fd={fd}");
        string response = "{\"code\":200,\"sys\":{\"heartbeat\":10,\"dict\":{},\"protos\":{\"client\":{},\"server\":{}}},\"user\":{}}";
        byte[] responseBody = Encoding.UTF8.GetBytes(response);
        byte[] responsePkg = Package.Encode(PackageType.Handshake, responseBody);
        Console.WriteLine($"[GatewayService] Sending handshake response, size={responsePkg.Length}");
        Send(fd, responsePkg);

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

    private void HandleHeartbeat(int fd)
    {
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(fd, out var session))
            {
                session.LastHeartbeat = DateTime.UtcNow;
            }
        }

        byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
        Send(fd, heartbeatPkg);
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

        var msg = Message.Decode(body);
        if (msg == null)
        {
            Console.WriteLine($"[GatewayService] Failed to decode message, body_size={body.Length}");
            return;
        }

        string msgBody = msg.Body.Length > 0 ? Encoding.UTF8.GetString(msg.Body) : "{}";
        if (msg.Type == MessageType.Request)
        {
            HandleRequest(fd, msg.Id, msg.Route, msgBody);
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

    private void HandleRequest(int fd, int id, string route, string body)
    {
        Console.WriteLine($"[GatewayService] handle_request fd={fd}, id={id}, route={route}, body={body}");
        string responseBody;

        lock (HandlersLock)
        {
            if (Handlers.TryGetValue(route, out var handler))
            {
                Session? session;
                lock (_sessionsLock)
                {
                    if (!_sessions.TryGetValue(fd, out session))
                    {
                        Console.WriteLine($"[GatewayService] Session not found for fd={fd}");
                        return;
                    }
                }

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
                    byte[] errorResponseMsg = Message.Encode(id, MessageType.Response, false, "", errorResponseBytes);
                    byte[] errorResponsePkg = Package.Encode(PackageType.Data, errorResponseMsg);
                    Console.WriteLine($"[GatewayService] Sending error response, pkg_size={errorResponsePkg.Length}");
                    Send(fd, errorResponsePkg);
                    return;
                }

                responseBody = handler(session, bodyJson);
            }
            else
            {
                Console.WriteLine($"[GatewayService] Unknown route: {route}");
                responseBody = $"{{\"code\":404,\"msg\":\"Route not found: {route}\"}}";
            }
        }

        byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
        byte[] responseMsg = Message.Encode(id, MessageType.Response, false, "", responseBytes);
        byte[] responsePkg = Package.Encode(PackageType.Data, responseMsg);
        Send(fd, responsePkg);
    }

    private void Send(int fd, byte[] data)
    {
        try
        {
            var socket = Starnet.Instance.GetSocket(fd);
            if (socket == null) return;

            int sent = 0;
            while (sent < data.Length)
            {
                try
                {
                    int n = socket.Send(data, sent, data.Length - sent, SocketFlags.None);
                    if (n <= 0)
                    {
                        Console.WriteLine($"[GatewayService] Send returned 0, fd={fd}");
                        OnSocketClose(fd);
                        Starnet.Instance.CloseConn(fd);
                        return;
                    }
                    sent += n;
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.WouldBlock)
                    {
                        Console.WriteLine($"[GatewayService] Send would block, fd={fd}, sent={sent}/{data.Length}");
                        // TODO: Add to write buffer and enable EPOLLOUT
                        return;
                    }
                    Console.WriteLine($"[GatewayService] Send error: {ex.SocketErrorCode}, fd={fd}");
                    OnSocketClose(fd);
                    Starnet.Instance.CloseConn(fd);
                    return;
                }
            }
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                Console.WriteLine($"[GatewayService] Send would block, fd={fd}");
                return;
            }
            Console.WriteLine($"[GatewayService] Send error: {ex.Message}, fd={fd}");
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

    private void SendHeartbeat(int fd)
    {
        byte[] heartbeatPkg = Package.Encode(PackageType.Heartbeat, null);
        Send(fd, heartbeatPkg);
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
                        SendHeartbeat(pair.Key);
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
