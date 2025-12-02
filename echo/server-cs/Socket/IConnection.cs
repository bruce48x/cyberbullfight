using System.Net;
using System.IO.Pipelines;

namespace ServerCs.Socket;

public enum TransportType { Tcp, Udp }

public interface IConnection : IAsyncDisposable
{
    PipeWriter Output { get; }
    PipeReader Input { get; }
    EndPoint RemoteEndPoint { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> data);
}