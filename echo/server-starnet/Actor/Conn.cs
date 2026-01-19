namespace ServerCs.Actor;

public enum ConnType : byte
{
    Listen = 1,
    Client = 2
}

public class Conn
{
    public ConnType Type { get; set; }
    public int Fd { get; set; }
    public uint ServiceId { get; set; }
}
