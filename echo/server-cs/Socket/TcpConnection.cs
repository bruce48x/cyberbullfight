using System.Net;
using System.Net.Sockets;
using System.IO.Pipelines;

namespace ServerCs.Socket;

public class TcpConnection : IConnection
{
    readonly System.Net.Sockets.Socket _socket;
    readonly Pipe _pipe;
    readonly SaeaPool _saeaPool;
    readonly SocketAsyncEventArgs _receiveArgs;
    bool _receiving;
    public PipeWriter Output => _pipe.Writer;
    public PipeReader Input => _pipe.Reader;
    public EndPoint RemoteEndPoint => _socket.RemoteEndPoint;

    public TcpConnection(System.Net.Sockets.Socket s, SaeaPool pool)
    {
        _socket = s;
        _pipe = new Pipe();
        _saeaPool = pool;
        _receiveArgs = pool.Rent();
        _receiveArgs.AcceptSocket = _socket;
    }

    public Task StartReceivingAsync() => ReceiveLoop();

    async Task ReceiveLoop()
    {
        while (true)
        {
            var memory = _pipe.Writer.GetMemory(8192);
            int read;
            try
            {
                read = await _socket.ReceiveAsync(memory, SocketFlags.None);
            }
            catch (SocketException) { break; }
            if (read == 0) break; // closed
            _pipe.Writer.Advance(read);
            var flush = await _pipe.Writer.FlushAsync();
            if (flush.IsCompleted) break;
            // let consumer parse from _pipe.Reader
        }
        await _pipe.Writer.CompleteAsync();
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
    {
        // for high perf, avoid awaiting when completed synchronously
        await _socket.SendAsync(data, SocketFlags.None);
    }

    public async ValueTask DisposeAsync()
    {
        try { _socket.Shutdown(SocketShutdown.Both); } catch {}
        _socket.Close();
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
    }
}