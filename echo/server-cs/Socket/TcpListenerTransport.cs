using System.Net;
using System.Net.Sockets;

namespace ServerCs.Socket;

public class TcpListenerTransport : IListener
{
    readonly IPEndPoint _endpoint;
    readonly System.Net.Sockets.Socket _listen;
    readonly SaeaPool _saeaPool;
    readonly Func<IConnection, Task> _onConnection;

    public TcpListenerTransport(IPEndPoint endpoint, SaeaPool pool, Func<IConnection, Task> onConn)
    {
        _endpoint = endpoint;
        _saeaPool = pool;
        _onConnection = onConn;
        _listen = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listen.Bind(_endpoint);
        _listen.Listen(1024);
        while (!ct.IsCancellationRequested)
        {
            System.Net.Sockets.Socket accepted;
            try
            {
                accepted = await _listen.AcceptAsync();
            }
            catch (Exception) { break; }
            ConfigureSocket(accepted);
            var conn = new TcpConnection(accepted, _saeaPool);
            _ = _onConnection(conn); // fire-and-forget application handler
            _ = conn.StartReceivingAsync(); // start recv loop
        }
    }

    void ConfigureSocket(System.Net.Sockets.Socket s)
    {
        s.NoDelay = true;
        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        // tune recv/send buffers if desired
    }

    public ValueTask DisposeAsync()
    {
        _listen.Close();
        return default;
    }
}