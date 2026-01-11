using System.IO.Pipelines;
using System.Net;

namespace ServerCs.Socket;

public enum TransportType
{
    Tcp,
    Udp
}

public interface IConnection : IAsyncDisposable
{
    PipeWriter Output { get; }
    PipeReader Input { get; }
    EndPoint RemoteEndPoint { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> data);
}