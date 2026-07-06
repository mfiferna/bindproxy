using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Proxy;

/// <summary>Handles one accepted client connection end-to-end.</summary>
internal static class ProxyConnection
{
    private const int MaxHeadBytes = 32 * 1024;
    private enum ReadHeadStatus { Completed, ClientClosed, TooLarge }

    public static async Task RunAsync(
        TcpClient client,
        IPAddress outboundAddress,
        IDnsResolver resolver,
        TimeSpan connectTimeout,
        Action<string> onError,
        Action onSuccess,
        CancellationToken ct)
    {
        var clientStream = client.GetStream();
        var (status, head, leftover) = await ReadHeadAsync(clientStream, ct).ConfigureAwait(false);
        if (status == ReadHeadStatus.ClientClosed) return;
        if (status == ReadHeadStatus.TooLarge)
        {
            onError("request head exceeded 32 KiB");
            await WriteErrorAsync(clientStream, "431 Request Header Fields Too Large", "Request head exceeded 32 KiB", ct).ConfigureAwait(false);
            return;
        }

        if (!HttpProxyRequest.TryParse(head, out var request, out var parseError))
        {
            onError($"bad request: {parseError}");
            await WriteErrorAsync(clientStream, "400 Bad Request", parseError, ct).ConfigureAwait(false);
            return;
        }

        IPAddress remoteAddress;
        try
        {
            remoteAddress = await resolver.ResolveAsync(request.Host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onError($"DNS: {ex.Message}");
            await WriteErrorAsync(clientStream, "502 Bad Gateway", $"DNS resolution failed: {ex.Message}", ct).ConfigureAwait(false);
            return;
        }

        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // The bind below is the entire point of this app: it pins the outbound
            // connection to the selected NIC.
            server.Bind(new IPEndPoint(outboundAddress, 0));
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(connectTimeout);
            await server.ConnectAsync(new IPEndPoint(remoteAddress, request.Port), connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            onError($"connect timeout: {request.Host}:{request.Port}");
            await WriteErrorAsync(clientStream, "504 Gateway Timeout", $"Connect to {request.Host}:{request.Port} timed out", ct).ConfigureAwait(false);
            return;
        }
        catch (SocketException ex)
        {
            onError($"connect {request.Host}:{request.Port}: {ex.SocketErrorCode}");
            await WriteErrorAsync(clientStream, "502 Bad Gateway", $"Connect failed: {ex.SocketErrorCode}", ct).ConfigureAwait(false);
            return;
        }

        onSuccess();
        await using var serverStream = new NetworkStream(server, ownsSocket: false);
        if (request.Kind == ProxyRequestKind.Connect)
        {
            await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct).ConfigureAwait(false);
        }
        else
        {
            await serverStream.WriteAsync(Encoding.ASCII.GetBytes(request.RewrittenHead), ct).ConfigureAwait(false);
        }
        if (leftover.Length > 0)
        {
            await serverStream.WriteAsync(leftover, ct).ConfigureAwait(false);
        }
        await PumpAsync(clientStream, serverStream, ct).ConfigureAwait(false);
    }

    private static async Task<(ReadHeadStatus Status, string Head, byte[] Leftover)> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeadBytes];
        int filled = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (read == 0) return (ReadHeadStatus.ClientClosed, string.Empty, []);
            filled += read;
            int end = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                string head = Encoding.ASCII.GetString(buffer, 0, end);
                byte[] leftover = buffer.AsSpan(end + 4, filled - end - 4).ToArray();
                return (ReadHeadStatus.Completed, head, leftover);
            }
            if (filled == buffer.Length) return (ReadHeadStatus.TooLarge, string.Empty, []);
        }
    }

    private static async Task PumpAsync(Stream a, Stream b, CancellationToken ct)
    {
        var ab = CopySilentlyAsync(a, b, ct);
        var ba = CopySilentlyAsync(b, a, ct);
        await Task.WhenAny(ab, ba).ConfigureAwait(false);
        // Closing both streams unblocks whichever direction is still reading.
        a.Dispose();
        b.Dispose();
        await Task.WhenAll(ab, ba).ConfigureAwait(false);
    }

    private static async Task CopySilentlyAsync(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, ct).ConfigureAwait(false); }
        catch { /* disconnects and cancellation end the pump; nothing to report */ }
    }

    private static async Task WriteErrorAsync(NetworkStream stream, string status, string? reason, CancellationToken ct)
    {
        string body = (reason ?? "") + "\n";
        string response = $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        try { await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct).ConfigureAwait(false); }
        catch { /* client already gone */ }
    }
}
