using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Tests;

/// <summary>IDnsResolver stub driven by a lambda.</summary>
internal sealed class StubResolver(Func<string, IPAddress> resolve) : IDnsResolver
{
    public Task<IPAddress> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(resolve(host));
}

/// <summary>
/// IWebProxy that always proxies. .NET's WebProxy silently bypasses proxies for loopback
/// destinations, which would make these tests dial the origin directly.
/// </summary>
internal sealed class FixedProxy(Uri proxyUri) : IWebProxy
{
    public ICredentials? Credentials { get; set; }
    public Uri GetProxy(Uri destination) => proxyUri;
    public bool IsBypassed(Uri host) => false;
}

/// <summary>Minimal HTTP origin: captures the request head, answers "hello", closes.</summary>
internal sealed class FakeOriginServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }
    public volatile string? LastRequestHead;

    public FakeOriginServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var buffer = new byte[8192];
            while (!head.ToString().Contains("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(buffer, _cts.Token);
                if (read == 0) return;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            LastRequestHead = head.ToString();
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"u8.ToArray();
            await stream.WriteAsync(response, _cts.Token);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}

internal sealed class BodyCaptureOriginServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }
    public volatile string? LastRequestHead;
    public volatile string? LastRequestBody;

    public BodyCaptureOriginServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var captured = new List<byte>();

            while (true)
            {
                int read = await stream.ReadAsync(buffer, _cts.Token);
                if (read == 0) return;
                captured.AddRange(buffer.AsSpan(0, read).ToArray());
                byte[] raw = captured.ToArray();
                int headEnd = raw.AsSpan().IndexOf("\r\n\r\n"u8);
                if (headEnd < 0) continue;

                LastRequestHead = Encoding.ASCII.GetString(raw, 0, headEnd + 4);
                int contentLength = GetContentLength(LastRequestHead);
                int bodyOffset = headEnd + 4;
                while (captured.Count - bodyOffset < contentLength)
                {
                    read = await stream.ReadAsync(buffer, _cts.Token);
                    if (read == 0) break;
                    captured.AddRange(buffer.AsSpan(0, read).ToArray());
                }

                raw = captured.ToArray();
                LastRequestBody = Encoding.ASCII.GetString(raw, bodyOffset, Math.Min(contentLength, raw.Length - bodyOffset));
                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), _cts.Token);
                return;
            }
        }
    }

    private static int GetContentLength(string head)
    {
        foreach (var line in head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Content-Length:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            return int.Parse(line[prefix.Length..].Trim());
        }

        return 0;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}

internal static class RawSocket
{
    /// <summary>Reads from the stream until the header terminator, returns the head text.</summary>
    public static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var head = new StringBuilder();
        var buffer = new byte[1024];
        while (!head.ToString().Contains("\r\n\r\n"))
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return head.ToString();
    }
}
