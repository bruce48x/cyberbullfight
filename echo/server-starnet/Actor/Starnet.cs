using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ServerCs.Actor.Message;

namespace ServerCs.Actor;

public class Starnet
{
    public static Starnet Instance { get; private set; } = null!;

    private readonly ConcurrentDictionary<uint, Service> _services = new();
    private uint _maxId = 0;
    private readonly object _servicesLock = new();

    private const int WORKER_NUM = 3;
    private readonly List<Worker> _workers = new();
    private readonly List<Thread> _workerThreads = new();

    private readonly ConcurrentQueue<Service> _globalQueue = new();
    private readonly object _globalLock = new();
    private int _globalLen = 0;

    private readonly AutoResetEvent _sleepEvent = new(false);
    private int _sleepCount = 0;
    private readonly object _sleepLock = new();

    private SocketWorker? _socketWorker;
    private Thread? _socketThread;

    private readonly ConcurrentDictionary<int, Conn> _conns = new();
    private readonly ConcurrentDictionary<int, System.Net.Sockets.Socket> _sockets = new();
    private readonly object _connsLock = new();

    private volatile bool _running = true;

    public Starnet()
    {
        Instance = this;
    }

    public void Start()
    {
        Console.WriteLine("hello Starnet");

        StartWorker();
        StartSocket();
    }

    private void StartSocket()
    {
        _socketWorker = new SocketWorker();
        _socketWorker.Init();
        _socketThread = new Thread(_socketWorker.Run);
        _socketThread.Start();
    }

    private void StartWorker()
    {
        for (int i = 0; i < WORKER_NUM; i++)
        {
            Console.WriteLine($"start worker thread: {i}");
            var worker = new Worker(i, 2 << i);
            _workers.Add(worker);
            var thread = new Thread(worker.Run);
            thread.Start();
            _workerThreads.Add(thread);
        }
    }


    public void Stop()
    {
        _running = false;
        
        // Stop socket worker
        _socketWorker?.Stop();
        
        // Wake up all waiting workers
        for (int i = 0; i < WORKER_NUM; i++)
        {
            _sleepEvent.Set();
        }
    }

    public bool IsRunning => _running;

    public void Wait()
    {
        // Wait for all worker threads
        foreach (var thread in _workerThreads)
        {
            thread.Join();
        }
        
        // Wait for socket thread
        _socketThread?.Join();
    }

    public uint NewService<T>(string type) where T : Service, new()
    {
        Service srv = new T();
        return NewService(type, srv);
    }

    public uint NewService(string type, Service service)
    {
        service.Type = type;

        lock (_servicesLock)
        {
            service.Id = _maxId;
            _maxId++;
            _services[service.Id] = service;
        }

        service.OnInit();
        return service.Id;
    }

    public uint NewService(string type)
    {
        // Default implementation creates a base Service
        return NewService<Service>(type);
    }

    public Service? GetService(uint id)
    {
        _services.TryGetValue(id, out var srv);
        return srv;
    }

    public void KillService(uint id)
    {
        var srv = GetService(id);
        if (srv == null)
        {
            return;
        }
        srv.OnExit();
        srv.IsExiting = true;

        lock (_servicesLock)
        {
            _services.TryRemove(id, out _);
        }
    }

    public Service? PopGlobalQueue()
    {
        lock (_globalLock)
        {
            if (_globalQueue.TryDequeue(out var srv))
            {
                _globalLen--;
                return srv;
            }
        }
        return null;
    }

    public void PushGlobalQueue(Service srv)
    {
        lock (_globalLock)
        {
            _globalQueue.Enqueue(srv);
            _globalLen++;
        }
    }

    public void Send(uint toId, BaseMsg msg)
    {
        var toSrv = GetService(toId);
        if (toSrv == null)
        {
            Console.WriteLine($"Send fail, toSrv not exist toId: {toId}");
            return;
        }

        toSrv.PushMsg(msg);

        bool hasPush = false;
        if (!toSrv.InGlobal)
        {
            PushGlobalQueue(toSrv);
            toSrv.SetInGlobal(true);
            hasPush = true;
        }

        if (hasPush)
        {
            CheckAndWakeUp();
        }
    }

    public void CheckAndWakeUp()
    {
        lock (_sleepLock)
        {
            if (_sleepCount == 0)
            {
                return;
            }
            if (WORKER_NUM - _sleepCount <= _globalLen)
            {
                _sleepEvent.Set();
            }
        }
    }

    public void WorkerWait()
    {
        if (!_running)
        {
            return;
        }
        
        lock (_sleepLock)
        {
            _sleepCount++;
        }
        _sleepEvent.WaitOne();
        lock (_sleepLock)
        {
            _sleepCount--;
        }
    }

    public int AddConn(int fd, uint serviceId, ConnType type)
    {
        var conn = new Conn
        {
            Fd = fd,
            ServiceId = serviceId,
            Type = type
        };
        _conns[fd] = conn;
        return fd;
    }

    public Conn? GetConn(int fd)
    {
        _conns.TryGetValue(fd, out var conn);
        return conn;
    }

    public bool RemoveConn(int fd)
    {
        return _conns.TryRemove(fd, out _);
    }

    public void RegisterSocket(int fd, System.Net.Sockets.Socket socket)
    {
        _sockets[fd] = socket;
    }

    public System.Net.Sockets.Socket? GetSocket(int fd)
    {
        _sockets.TryGetValue(fd, out var socket);
        return socket;
    }

    public void UnregisterSocket(int fd)
    {
        _sockets.TryRemove(fd, out _);
    }

    public int Listen(uint port, uint serviceId)
    {
        var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Blocking = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        var endPoint = new IPEndPoint(IPAddress.Any, (int)port);
        try
        {
            socket.Bind(endPoint);
            socket.Listen(1024);

            int fd = socket.Handle.ToInt32();
            AddConn(fd, serviceId, ConnType.Listen);
            RegisterSocket(fd, socket);

            _socketWorker?.AddEvent(fd);

            return fd;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Listen error: {ex.Message}");
            socket.Close();
            return -1;
        }
    }

    public void CloseConn(int fd)
    {
        bool succ = RemoveConn(fd);
        if (_sockets.TryRemove(fd, out var socket))
        {
            try
            {
                socket.Close();
            }
            catch { }
        }
        if (succ)
        {
            _socketWorker?.RemoveEvent(fd);
        }
    }
}
