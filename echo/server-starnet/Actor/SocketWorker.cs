using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace ServerCs.Actor;

public class SocketWorker
{
    private readonly ConcurrentDictionary<int, SocketEvent> _events = new();
    private volatile bool _running = true;

    public void Init()
    {
        Console.WriteLine("SocketWorker Init");
    }

    public void Stop()
    {
        _running = false;
    }

    public void Run()
    {
        Console.WriteLine("[SocketWorker] Started");
        while (_running)
        {
            ProcessEvents();
            Thread.Sleep(1); // Small delay to prevent CPU spinning
        }
        Console.WriteLine("[SocketWorker] Stopped");
    }

    private void ProcessEvents()
    {
        var socketsToCheck = new List<System.Net.Sockets.Socket>();
        var fdsToCheck = new List<int>();

        foreach (var kvp in _events)
        {
            if (kvp.Value.Socket != null)
            {
                socketsToCheck.Add(kvp.Value.Socket);
                fdsToCheck.Add(kvp.Key);
            }
        }

        if (socketsToCheck.Count == 0)
        {
            return;
        }

        try
        {
            if (socketsToCheck.Count == 0) return;

            var readList = new List<System.Net.Sockets.Socket>(socketsToCheck);
            var writeList = new List<System.Net.Sockets.Socket>();
            var errorList = new List<System.Net.Sockets.Socket>(socketsToCheck);

            System.Net.Sockets.Socket.Select(readList, writeList, errorList, 1); // 1ms timeout

            foreach (var socket in readList)
            {
                int fd = socket.Handle.ToInt32();
                var conn = Starnet.Instance.GetConn(fd);
                if (conn == null) continue;

                if (conn.Type == ConnType.Listen)
                {
                    OnAccept(conn);
                }
                else
                {
                    OnRW(conn, true, false);
                }
            }

            foreach (var socket in writeList)
            {
                int fd = socket.Handle.ToInt32();
                var conn = Starnet.Instance.GetConn(fd);
                if (conn != null && conn.Type == ConnType.Client)
                {
                    OnRW(conn, false, true);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SocketWorker error: {ex.Message}");
        }
    }

    public void AddEvent(int fd)
    {
        Console.WriteLine($"AddEvent fd {fd}");
        var socket = Starnet.Instance.GetSocket(fd);
        if (socket != null)
        {
            _events[fd] = new SocketEvent { Fd = fd, Socket = socket };
        }
    }

    public void RemoveEvent(int fd)
    {
        Console.WriteLine($"RemoveEvent fd {fd}");
        _events.TryRemove(fd, out _);
    }

    public void ModifyEvent(int fd, bool epollOut)
    {
        Console.WriteLine($"ModifyEvent fd {fd} {epollOut}");
        // In C#, we don't need to modify events like epoll
        // The socket is already set up for both read and write
    }

    private void OnAccept(Conn conn)
    {
        Console.WriteLine($"OnAccept fd: {conn.Fd}");
        var listenSocket = Starnet.Instance.GetSocket(conn.Fd);
        if (listenSocket == null) return;

        // Accept all pending connections in a loop
        // This is critical when multiple connections arrive simultaneously
        while (true)
        {
            try
            {
                var clientSocket = listenSocket.Accept();
                if (clientSocket == null) break;

                clientSocket.Blocking = false;
                clientSocket.NoDelay = true;

                int clientFd = clientSocket.Handle.ToInt32();
                Starnet.Instance.AddConn(clientFd, conn.ServiceId, ConnType.Client);
                Starnet.Instance.RegisterSocket(clientFd, clientSocket);
                AddEvent(clientFd);

                var msg = new SocketAcceptMsg
                {
                    Type = MsgType.SocketAccept,
                    ListenFd = conn.Fd,
                    ClientFd = clientFd
                };
                Starnet.Instance.Send(conn.ServiceId, msg);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    // No more connections to accept
                    break;
                }
                Console.WriteLine($"OnAccept error: {ex.Message}, SocketErrorCode: {ex.SocketErrorCode}");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnAccept unexpected error: {ex.Message}");
                break;
            }
        }
    }

    private void OnRW(Conn conn, bool r, bool w)
    {
        int fd = conn.Fd;
        var socket = Starnet.Instance.GetSocket(fd);
        if (socket == null) return;

        // Get the service (should be GatewayService)
        var service = Starnet.Instance.GetService(conn.ServiceId);
        if (service is GatewayService gatewayService)
        {
            if (r)
            {
                // Read from socket and write to Pipe
                gatewayService.OnSocketRead(fd, socket);
            }
            if (w)
            {
                // Write from Pipe to socket (if needed)
                gatewayService.OnSocketWrite(fd, socket);
            }
        }
    }

    private class SocketEvent
    {
        public int Fd { get; set; }
        public System.Net.Sockets.Socket? Socket { get; set; }
    }
}
