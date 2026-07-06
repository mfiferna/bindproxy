using System.Net;
using System.Net.Sockets;
using BindProxy.Core.Dns;
using Xunit;

namespace BindProxy.Core.Tests;

public class DnsResolverTests
{
    private static readonly TimeSpan FastTimeout = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Resolves_ip_literal_without_any_servers()
    {
        var resolver = new DnsResolver(IPAddress.Loopback, []);
        var result = await resolver.ResolveAsync("192.168.1.5", CancellationToken.None);
        Assert.Equal(IPAddress.Parse("192.168.1.5"), result);
    }

    [Fact]
    public async Task Resolves_via_dns_server()
    {
        using var server = new FakeDnsServer(IPAddress.Parse("10.0.0.42"));
        var resolver = new DnsResolver(IPAddress.Loopback, [server.EndPoint]);
        var result = await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(IPAddress.Parse("10.0.0.42"), result);
    }

    [Fact]
    public async Task Caches_positive_answers()
    {
        using var server = new FakeDnsServer(IPAddress.Parse("10.0.0.42"), ttlSeconds: 60);
        var resolver = new DnsResolver(IPAddress.Loopback, [server.EndPoint]);
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(1, server.QueryCount);
    }

    [Fact]
    public async Task Does_not_cache_zero_ttl()
    {
        using var server = new FakeDnsServer(IPAddress.Parse("10.0.0.42"), ttlSeconds: 0);
        var resolver = new DnsResolver(IPAddress.Loopback, [server.EndPoint]);
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(2, server.QueryCount);
    }

    [Fact]
    public async Task FlushCache_forces_requery()
    {
        using var server = new FakeDnsServer(IPAddress.Parse("10.0.0.42"), ttlSeconds: 60);
        var resolver = new DnsResolver(IPAddress.Loopback, [server.EndPoint]);
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        resolver.FlushCache();
        await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(2, server.QueryCount);
    }

    [Fact]
    public async Task Falls_back_to_second_server_on_timeout()
    {
        // A bound socket that never answers stands in for a dead DNS server.
        using var dead = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var deadEndPoint = (IPEndPoint)dead.Client.LocalEndPoint!;
        using var live = new FakeDnsServer(IPAddress.Parse("10.0.0.42"));
        var resolver = new DnsResolver(IPAddress.Loopback, [deadEndPoint, live.EndPoint], FastTimeout);
        var result = await resolver.ResolveAsync("example.com", CancellationToken.None);
        Assert.Equal(IPAddress.Parse("10.0.0.42"), result);
    }

    [Fact]
    public async Task Throws_when_no_servers_configured()
    {
        var resolver = new DnsResolver(IPAddress.Loopback, []);
        var ex = await Assert.ThrowsAsync<DnsResolutionException>(
            () => resolver.ResolveAsync("example.com", CancellationToken.None));
        Assert.Equal("example.com", ex.Host);
    }

    [Fact]
    public async Task Throws_when_all_servers_time_out()
    {
        using var dead = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var deadEndPoint = (IPEndPoint)dead.Client.LocalEndPoint!;
        var resolver = new DnsResolver(IPAddress.Loopback, [deadEndPoint], FastTimeout);
        await Assert.ThrowsAsync<DnsResolutionException>(
            () => resolver.ResolveAsync("example.com", CancellationToken.None));
    }

    [Fact]
    public async Task Ignores_forged_response_from_a_different_source_and_times_out()
    {
        // "silent" stands in for the configured DNS server: it receives the query but never
        // replies. "attacker" is a distinct socket (different local port) that races in a
        // well-formed, correctly-keyed response as soon as it sees the query go out - simulating
        // an off-path spoofer that guessed the query's transaction id and the resolver's ephemeral
        // port. A resolver that doesn't verify the reply's source would accept this.
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var silentEndPoint = (IPEndPoint)silent.Client.LocalEndPoint!;
        using var attacker = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        _ = Task.Run(async () =>
        {
            var request = await silent.ReceiveAsync();
            var forged = DnsTestData.BuildResponse(request.Buffer, IPAddress.Parse("6.6.6.6"), 60);
            await attacker.SendAsync(forged, request.RemoteEndPoint);
        });

        var resolver = new DnsResolver(IPAddress.Loopback, [silentEndPoint], FastTimeout);
        await Assert.ThrowsAsync<DnsResolutionException>(
            () => resolver.ResolveAsync("example.com", CancellationToken.None));
    }
}
