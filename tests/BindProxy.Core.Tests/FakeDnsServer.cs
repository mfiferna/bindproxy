using System.Net;
using System.Net.Sockets;

namespace BindProxy.Core.Tests;

/// <summary>A loopback UDP server answering every A query with a fixed address.</summary>
internal sealed class FakeDnsServer : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly IPAddress _answer;
    private readonly int _ttlSeconds;
    private int _queryCount;

    public IPEndPoint EndPoint { get; }
    public int QueryCount => Volatile.Read(ref _queryCount);

    public FakeDnsServer(IPAddress answer, int ttlSeconds = 60)
    {
        _answer = answer;
        _ttlSeconds = ttlSeconds;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var request = await _udp.ReceiveAsync(_cts.Token);
                Interlocked.Increment(ref _queryCount);
                var response = DnsTestData.BuildResponse(request.Buffer, _answer, _ttlSeconds);
                await _udp.SendAsync(response, request.RemoteEndPoint, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}
