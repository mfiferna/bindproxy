namespace BindProxy.Core.Dns;

/// <summary>A malformed or error DNS response.</summary>
public sealed class DnsException(string message) : Exception(message);

/// <summary>Resolution failed after trying all configured servers.</summary>
public sealed class DnsResolutionException(string host, string reason)
    : Exception($"DNS resolution failed for '{host}': {reason}")
{
    public string Host { get; } = host;
}
