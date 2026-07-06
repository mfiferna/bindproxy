using System.Net;
using System.Net.Sockets;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Proxy;

/// <summary>
/// An HTTP forward proxy on 127.0.0.1 whose outbound connections are bound to one NIC's address.
/// One instance per session/NIC.
/// </summary>
public sealed class ProxyServer(IPAddress outboundAddress, IDnsResolver resolver, TimeSpan? connectTimeout = null) : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private int _activeConnections;
    private readonly TimeSpan _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public event Action? ActiveConnectionsChanged;
    /// <summary>Raised with a short message when a connection fails before its tunnel is established.</summary>
    public event Action<string>? ConnectionError;
    /// <summary>Raised when an outbound connection is established (used to clear error states).</summary>
    public event Action? ConnectionSucceeded;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        Interlocked.Increment(ref _activeConnections);
        ActiveConnectionsChanged?.Invoke();
        try
        {
            await ProxyConnection.RunAsync(
                client, outboundAddress, resolver, _connectTimeout,
                msg => ConnectionError?.Invoke(msg),
                () => ConnectionSucceeded?.Invoke(),
                _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ConnectionError?.Invoke($"internal proxy error: {ex.Message}");
        }
        finally
        {
            client.Dispose();
            Interlocked.Decrement(ref _activeConnections);
            ActiveConnectionsChanged?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}
