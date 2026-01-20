namespace ServerCs.Actor.Message;

public class ServiceMsg : BaseMsg
{
    public uint Source { get; set; }
    public byte[]? Buff { get; set; }
    public int Size { get; set; }
}