using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

namespace ServerCs.Socket;

public class TcpConnection : IConnection
{
    private readonly Pipe _pipe;
    private readonly System.Net.Sockets.Socket _socket;
    private bool _disposed;

    public TcpConnection(System.Net.Sockets.Socket s)
    {
        _socket = s;
        _pipe = new Pipe();
    }

    public PipeWriter Output => _pipe.Writer;
    public PipeReader Input => _pipe.Reader;
    public EndPoint RemoteEndPoint => _socket.RemoteEndPoint;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data)
    {
        if (_disposed) return;

        await _socket.SendAsync(data);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Shutdown socket to stop receiving
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch
        {
        }

        try
        {
            _socket.Close();
        }
        catch
        {
        }

        // Complete pipes to signal ReceiveLoop to exit
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
    }

    public async Task StartReceivingAsync()
    {
        const int minimumBufferSize = 1024;
        try
        {
            while (!_disposed)
            {
                var memory = _pipe.Writer.GetMemory(minimumBufferSize);
                var bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None);
                if (bytesRead == 0) break;

                // Tell the PipeWriter how much was read from the Socket
                _pipe.Writer.Advance(bytesRead);


                // Make the data available to the PipeReader
                var result = await _pipe.Writer.FlushAsync();

                if (result.IsCompleted) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ReceiveLoop: {ex.Message}");
        }
        finally
        {
            await _pipe.Writer.CompleteAsync();
        }
    }
}