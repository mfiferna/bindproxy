using System.Net;

namespace BindProxy.Core.Nics;

/// <summary>
/// A network interface suitable for default proxy use: up, non-loopback, with an IPv4 address and
/// an IPv4 gateway so outbound internet-bound traffic can actually route through it.
/// </summary>
public sealed record NicInfo(
    string Id,
    string Name,
    string Description,
    IPAddress Ipv4Address,
    IReadOnlyList<IPAddress> DnsServers);
