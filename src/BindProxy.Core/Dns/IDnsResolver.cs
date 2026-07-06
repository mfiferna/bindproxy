using System.Net;

namespace BindProxy.Core.Dns;

public interface IDnsResolver
{
    /// <summary>Resolves a hostname (or parses an IP literal) to a single IPv4 address.</summary>
    Task<IPAddress> ResolveAsync(string host, CancellationToken ct);
}
