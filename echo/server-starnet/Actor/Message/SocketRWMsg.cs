namespace ServerCs.Actor.Message;

public class SocketRWMsg : BaseMsg
{
    public int Fd { get; set; }
    public bool IsRead { get; set; }
    public bool IsWrite { get; set; }
}