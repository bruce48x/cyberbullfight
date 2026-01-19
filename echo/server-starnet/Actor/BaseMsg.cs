namespace ServerCs.Actor;

public enum MsgType : byte
{
    Service = 1,
    SocketAccept = 2,
    SocketRW = 3
}

public abstract class BaseMsg
{
    public MsgType Type { get; set; }
}

public class ServiceMsg : BaseMsg
{
    public uint Source { get; set; }
    public byte[]? Buff { get; set; }
    public int Size { get; set; }
}

public class SocketAcceptMsg : BaseMsg
{
    public int ListenFd { get; set; }
    public int ClientFd { get; set; }
}

public class SocketRWMsg : BaseMsg
{
    public int Fd { get; set; }
    public bool IsRead { get; set; }
    public bool IsWrite { get; set; }
}
