using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace ServerCs.Actor;

public enum ConnectionState
{
    Inited,
    WaitAck,
    Working,
    Closed
}

public class Session
{
    public int Fd { get; set; }
    public ConnectionState State { get; set; } = ConnectionState.Inited;
    public Pipe Pipe { get; set; } = new Pipe();
    public int ReqId { get; set; } = 0;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public readonly object ProcessLock = new(); // Lock for processing packets to ensure serialization per connection
}
