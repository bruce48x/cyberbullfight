namespace ServerCs.Actor.Message;

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
