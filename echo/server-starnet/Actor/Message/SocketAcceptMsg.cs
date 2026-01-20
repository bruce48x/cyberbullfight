namespace ServerCs.Actor.Message;

public class SocketAcceptMsg : BaseMsg
{
    public int ListenFd { get; set; }
    public int ClientFd { get; set; }
}

