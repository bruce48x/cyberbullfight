using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace ServerCs.Socket;

public class UdpListenerTransport : IListener
{
    private readonly Func<EndPoint, ReadOnlyMemory<byte>, Task> _onDatagram;
    private readonly System.Net.Sockets.Socket _sock;

    public UdpListenerTransport(IPEndPoint ep, Func<EndPoint, ReadOnlyMemory<byte>, Task> onDatagram)
    {
        _onDatagram = onDatagram;
        _sock = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _sock.Bind(ep);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        while (!ct.IsCancellationRequested)
        {
            var seg = new ArraySegment<byte>(buffer);
            SocketReceiveFromResult res;
            try
            {
                res = await _sock.ReceiveFromAsync(seg, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0));
            }
            catch (SocketException)
            {
                break;
            }

            if (res.ReceivedBytes > 0)
                await _onDatagram(res.RemoteEndPoint, new ReadOnlyMemory<byte>(buffer, 0, res.ReceivedBytes));
        }
    }

    public ValueTask DisposeAsync()
    {
        _sock.Close();
        return default;
    }
}