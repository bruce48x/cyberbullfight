using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Buffers;

namespace ServerCs.Socket;

public class SaeaPool
{
    readonly ConcurrentBag<SocketAsyncEventArgs> _bag = new();
    readonly int _bufferSize;
    readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

    public SaeaPool(int bufferSize, int initial)
    {
        _bufferSize = bufferSize;
        for (int i=0;i<initial;i++)
            _bag.Add(CreateSaea());
    }

    SocketAsyncEventArgs CreateSaea()
    {
        var saea = new SocketAsyncEventArgs();
        var buffer = _pool.Rent(_bufferSize);
        saea.SetBuffer(buffer, 0, _bufferSize);
        saea.Completed += (s, e) => { /* completion handled by caller */ };
        saea.UserToken = buffer; // store for return
        return saea;
    }

    public SocketAsyncEventArgs Rent()
    {
        if (_bag.TryTake(out var s)) return s;
        return CreateSaea();
    }

    public void Return(SocketAsyncEventArgs s)
    {
        var buf = (byte[])s.UserToken;
        // clear only necessary fields
        s.AcceptSocket = null;
        s.RemoteEndPoint = null;
        // don't clear buffer contents for perf
        _bag.Add(s);
    }
}