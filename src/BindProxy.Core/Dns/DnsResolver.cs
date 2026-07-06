using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace BindProxy.Core.Dns;

/// <summary>
/// Resolves A records by querying <paramref name="servers"/> in order over UDP sockets bound to
/// <paramref name="localAddress"/>, so DNS traffic leaves through the same NIC as proxied traffic.
/// </summary>
public sealed class DnsResolver(IPAddress localAddress, IReadOnlyList<IPEndPoint> servers, TimeSpan? timeout = null)
    : IDnsResolver
{
    private static readonly TimeSpan MaxCacheTtl = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(IPAddress Address, DateTime ExpiresUtc);

    public async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return literal;
        if (_cache.TryGetValue(host, out var hit) && hit.ExpiresUtc > DateTime.UtcNow) return hit.Address;
        if (servers.Count == 0)
            throw new DnsResolutionException(host, "no DNS servers configured for this NIC (set a DNS override)");

        Exception? lastFailure = null;
        foreach (var server in servers)
        {
            try
            {
                var (address, ttl) = await QueryServerAsync(server, host, ct).ConfigureAwait(false);
                var cacheTtl = ttl > MaxCacheTtl ? MaxCacheTtl : ttl;
                if (cacheTtl > TimeSpan.Zero)
                    _cache[host] = new CacheEntry(address, DateTime.UtcNow + cacheTtl);
                return address;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lastFailure = ex; // timeout or bad response: try the next server
            }
        }
        throw new DnsResolutionException(host, lastFailure is OperationCanceledException
            ? "timed out waiting for a response"
            : lastFailure?.Message ?? "all DNS servers failed");
    }

    private async Task<(IPAddress Address, TimeSpan Ttl)> QueryServerAsync(IPEndPoint server, string host, CancellationToken ct)
    {
        var id = (ushort)Random.Shared.Next(ushort.MaxValue + 1);
        var query = DnsMessage.BuildQuery(id, host);
        using var udp = new UdpClient(new IPEndPoint(localAddress, 0));
        // Connecting the socket makes the OS drop any datagram not from `server`, so a spoofed
        // reply from another source (guessing the transaction id and our ephemeral port) is
        // never delivered to ReceiveAsync.
        udp.Connect(server);
        await udp.SendAsync(query, ct).ConfigureAwait(false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        var result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
        var answer = DnsMessage.Parse(result.Buffer, id);
        if (answer.Addresses.Count == 0) throw new DnsException($"No A records for '{host}'");
        return (answer.Addresses[0], answer.Ttl);
    }

    public void FlushCache() => _cache.Clear();
}
