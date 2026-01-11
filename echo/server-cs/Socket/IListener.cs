namespace ServerCs.Socket;

public interface IListener : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
}