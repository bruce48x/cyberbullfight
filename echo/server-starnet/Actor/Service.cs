using System.Collections.Concurrent;
using System.Net.Sockets;
using ServerCs.Actor.Message;

namespace ServerCs.Actor;

public class Service
{
    public uint Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsExiting { get; set; } = false;

    private readonly ConcurrentQueue<BaseMsg> _msgQueue = new();
    private volatile bool _inGlobal = false;
    private readonly object _inGlobalLock = new();

    public void PushMsg(BaseMsg msg)
    {
        _msgQueue.Enqueue(msg);
    }

    protected BaseMsg? PopMsg()
    {
        if (_msgQueue.TryDequeue(out var msg))
        {
            return msg;
        }
        return null;
    }

    public virtual void OnInit()
    {
        Console.WriteLine($"[{Id}] OnInit");
    }

    public virtual void OnMsg(BaseMsg msg)
    {
        switch (msg.Type)
        {
            case MsgType.Service:
                if (msg is ServiceMsg serviceMsg)
                {
                    OnServiceMsg(serviceMsg);
                }
                break;
            case MsgType.SocketAccept:
                if (msg is SocketAcceptMsg acceptMsg)
                {
                    OnAcceptMsg(acceptMsg);
                }
                break;
            case MsgType.SocketRW:
                if (msg is SocketRWMsg rwMsg)
                {
                    OnRWMsg(rwMsg);
                }
                break;
        }
    }

    public virtual void OnExit()
    {
        Console.WriteLine($"[{Id}] OnExit");
    }

    public bool ProcessMsg()
    {
        var msg = PopMsg();
        if (msg != null)
        {
            OnMsg(msg);
            return true;
        }
        return false;
    }

    public void ProcessMsgs(int max)
    {
        for (int i = 0; i < max; i++)
        {
            if (!ProcessMsg())
            {
                break;
            }
        }
    }

    public bool SetInGlobal(bool isIn)
    {
        lock (_inGlobalLock)
        {
            bool oldValue = _inGlobal;
            _inGlobal = isIn;
            return oldValue;
        }
    }

    public bool InGlobal
    {
        get
        {
            lock (_inGlobalLock)
            {
                return _inGlobal;
            }
        }
    }

    public bool HasMessages => !_msgQueue.IsEmpty;

    protected virtual void OnServiceMsg(ServiceMsg msg)
    {
        Console.WriteLine("OnServiceMsg");
    }

    protected virtual void OnAcceptMsg(SocketAcceptMsg msg)
    {
        Console.WriteLine($"OnAcceptMsg {msg.ClientFd}");
    }

    protected virtual void OnRWMsg(SocketRWMsg msg)
    {
        int fd = msg.Fd;
        if (msg.IsRead)
        {
            const int BUFFSIZE = 512;
            byte[] buff = new byte[BUFFSIZE];
            int len = 0;
            do
            {
                try
                {
                    var socket = Starnet.Instance.GetSocket(fd);
                    if (socket == null) break;
                    
                    len = socket.Receive(buff, 0, BUFFSIZE, System.Net.Sockets.SocketFlags.None);
                    if (len > 0)
                    {
                        OnSocketData(fd, buff, len);
                    }
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.WouldBlock &&
                        ex.SocketErrorCode != SocketError.TimedOut)
                    {
                        if (Starnet.Instance.GetConn(fd) != null)
                        {
                            OnSocketClose(fd);
                            Starnet.Instance.CloseConn(fd);
                        }
                    }
                    len = 0;
                }
                catch
                {
                    len = 0;
                }
            } while (len == BUFFSIZE);

            if (len <= 0)
            {
                if (Starnet.Instance.GetConn(fd) != null)
                {
                    OnSocketClose(fd);
                    Starnet.Instance.CloseConn(fd);
                }
            }
        }

        if (msg.IsWrite)
        {
            if (Starnet.Instance.GetConn(fd) != null)
            {
                OnSocketWritable(fd);
            }
        }
    }

    protected virtual void OnSocketData(int fd, byte[] buff, int len)
    {
        Console.WriteLine($"OnSocketData {fd} buff: {System.Text.Encoding.UTF8.GetString(buff, 0, len)}");
    }

    protected virtual void OnSocketWritable(int fd)
    {
        Console.WriteLine($"OnSocketWritable {fd}");
    }

    protected virtual void OnSocketClose(int fd)
    {
        Console.WriteLine($"OnSocketClose {fd}");
    }
}
