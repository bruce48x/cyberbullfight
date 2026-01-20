using System.Collections.Concurrent;
using System.Net.Sockets;
using ServerCs.Actor.Message;

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
        int emptyLoops = 0;
        while (_running)
        {
            bool hadEvents = ProcessEvents();
            
            // Adaptive sleep: if no events, gradually increase sleep time
            // If events were processed, reset counter for immediate next check
            if (!hadEvents)
            {
                emptyLoops++;
                if (emptyLoops > 10)
                {
                    Thread.Sleep(1); // Sleep only after many empty loops
                    emptyLoops = 0;
                }
            }
            else
            {
                emptyLoops = 0;
            }
        }
        Console.WriteLine("[SocketWorker] Stopped");
    }

    private bool ProcessEvents()
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
            return false;
        }

        bool hadEvents = false;
        try
        {
            var readList = new List<System.Net.Sockets.Socket>(socketsToCheck);
            var writeList = new List<System.Net.Sockets.Socket>();
            var errorList = new List<System.Net.Sockets.Socket>(socketsToCheck);

            // Use 0 timeout for non-blocking select to maximize throughput
            System.Net.Sockets.Socket.Select(readList, writeList, errorList, 0);
            
            if (readList.Count > 0 || writeList.Count > 0)
            {
                hadEvents = true;
            }

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
        
        return hadEvents;
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
        var listenSocket = Starnet.Instance.GetSocket(conn.Fd);
        if (listenSocket == null) return;

        // Accept all pending connections in a loop
        // This is critical when multiple connections arrive simultaneously
        int acceptedCount = 0;
        while (true)
        {
            try
            {
                var clientSocket = listenSocket.Accept();
                if (clientSocket == null) break;

                clientSocket.Blocking = false;
                clientSocket.NoDelay = true;
                clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

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
                acceptedCount++;
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
        
        if (acceptedCount > 0)
        {
            Console.WriteLine($"[SocketWorker] OnAccept accepted {acceptedCount} connections");
        }
    }

    private void OnRW(Conn conn, bool r, bool w)
    {
        // Send message to service instead of directly calling service methods
        // This keeps SocketWorker decoupled from specific Service implementations
        var msg = new SocketRWMsg
        {
            Type = MsgType.SocketRW,
            Fd = conn.Fd,
            IsRead = r,
            IsWrite = w
        };
        Starnet.Instance.Send(conn.ServiceId, msg);
    }

    private class SocketEvent
    {
        public int Fd { get; set; }
        public System.Net.Sockets.Socket? Socket { get; set; }
    }
}
