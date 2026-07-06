using System.Net;

namespace BindProxy.Core.Dns;

/// <summary>Lets a running proxy switch DNS servers atomically when the override changes.
/// Swapping discards the old resolver and therefore its cache.</summary>
public sealed class SwappableResolver(IDnsResolver initial) : IDnsResolver
{
    private volatile IDnsResolver _inner = initial;

    public void Swap(IDnsResolver next) => _inner = next;

    public Task<IPAddress> ResolveAsync(string host, CancellationToken ct) => _inner.ResolveAsync(host, ct);
}
