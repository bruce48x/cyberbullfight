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
    Task? _receiveLoopTask;
    bool _disposed;
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

    public Task StartReceivingAsync()
    {
        _receiveLoopTask = ReceiveLoop();
        return _receiveLoopTask;
    }

    async Task ReceiveLoop()
    {
        try
        {
            while (!_disposed)
            {
                // Use SAEA buffer directly
                var buffer = _receiveArgs.Buffer;
                if (buffer == null) break;
                _receiveArgs.SetBuffer(0, buffer.Length);
                
                int read = await ReceiveAsync(_receiveArgs);
                if (read == 0 || _disposed) break; // closed

                // Copy from SAEA buffer to pipe
                var memory = _pipe.Writer.GetMemory(read);
                buffer.AsSpan(0, read).CopyTo(memory.Span);
                _pipe.Writer.Advance(read);
                
                var flush = await _pipe.Writer.FlushAsync();
                if (flush.IsCompleted) break;
            }
        }
        catch (SocketException) { }
        finally
        {
            await _pipe.Writer.CompleteAsync();
        }
    }

    Task<int> ReceiveAsync(SocketAsyncEventArgs args)
    {
        var tcs = new TaskCompletionSource<int>();
        EventHandler<SocketAsyncEventArgs>? handler = null;
        
        handler = (s, e) =>
        {
            args.Completed -= handler;
            if (e.SocketError == SocketError.Success)
                tcs.SetResult(e.BytesTransferred);
            else
                tcs.SetException(new SocketException((int)e.SocketError));
        };

        args.Completed += handler;
        
        if (!_socket.ReceiveAsync(args))
        {
            // Completed synchronously
            args.Completed -= handler;
            if (args.SocketError == SocketError.Success)
                return Task.FromResult(args.BytesTransferred);
            return Task.FromException<int>(new SocketException((int)args.SocketError));
        }

        return tcs.Task;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
    {
        if (_disposed) return;

        int offset = 0;
        while (offset < data.Length)
        {
            var sendArgs = _saeaPool.Rent();
            try
            {
                sendArgs.AcceptSocket = _socket;
                
                var buffer = sendArgs.Buffer;
                if (buffer == null) break;
                
                int chunkSize = Math.Min(data.Length - offset, buffer.Length);
                data.Slice(offset, chunkSize).CopyTo(buffer);
                sendArgs.SetBuffer(0, chunkSize);

                await SendAsync(sendArgs);
                offset += chunkSize;
            }
            finally
            {
                _saeaPool.Return(sendArgs);
            }
        }
    }

    Task SendAsync(SocketAsyncEventArgs args)
    {
        var tcs = new TaskCompletionSource();
        EventHandler<SocketAsyncEventArgs>? handler = null;
        
        handler = (s, e) =>
        {
            args.Completed -= handler;
            if (e.SocketError == SocketError.Success)
                tcs.SetResult();
            else
                tcs.SetException(new SocketException((int)e.SocketError));
        };

        args.Completed += handler;
        
        if (!_socket.SendAsync(args))
        {
            // Completed synchronously
            args.Completed -= handler;
            if (args.SocketError == SocketError.Success)
                return Task.CompletedTask;
            return Task.FromException(new SocketException((int)args.SocketError));
        }

        return tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Shutdown socket to stop receiving
        try { _socket.Shutdown(SocketShutdown.Both); } catch {}
        try { _socket.Close(); } catch {}
        
        // Complete pipes to signal ReceiveLoop to exit
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
        
        // Wait for ReceiveLoop to finish to ensure no one is using _receiveArgs
        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch { } // Ignore exceptions from receive loop
        }
        
        // Now it's safe to return SAEA to pool
        _saeaPool.Return(_receiveArgs);
    }
}