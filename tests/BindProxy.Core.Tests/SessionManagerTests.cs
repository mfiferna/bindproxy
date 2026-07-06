using System.Net;
using System.Net.Sockets;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Xunit;

namespace BindProxy.Core.Tests;

public class SessionManagerTests
{
    private static NicInfo LoopbackNic(string id = "test-nic") =>
        new(id, "Test", "Test NIC", IPAddress.Loopback, []);

    [Fact]
    public async Task GetOrStart_returns_same_session_for_same_nic()
    {
        await using var manager = new SessionManager();
        var a = manager.GetOrStart(LoopbackNic());
        var b = manager.GetOrStart(LoopbackNic());
        Assert.Same(a, b);
        Assert.Single(manager.Sessions);
    }

    [Fact]
    public async Task Sessions_for_different_nics_get_distinct_ports()
    {
        await using var manager = new SessionManager();
        var a = manager.GetOrStart(LoopbackNic("nic-a"));
        var b = manager.GetOrStart(LoopbackNic("nic-b"));
        Assert.NotEqual(a.Port, b.Port);
        Assert.Equal($"http://127.0.0.1:{a.Port}", a.ProxyUrl);
    }

    [Fact]
    public async Task Session_proxy_accepts_connections()
    {
        await using var manager = new SessionManager();
        var session = manager.GetOrStart(LoopbackNic());
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, session.Port);
        Assert.True(client.Connected);
    }

    [Fact]
    public async Task StopAsync_removes_session_and_closes_port()
    {
        await using var manager = new SessionManager();
        var session = manager.GetOrStart(LoopbackNic());
        int port = session.Port;
        await manager.StopAsync("test-nic");
        Assert.Empty(manager.Sessions);
        Assert.Null(manager.GetSession("test-nic"));
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task StopAsync_for_unknown_nic_is_a_no_op()
    {
        await using var manager = new SessionManager();
        await manager.StopAsync("nope");
    }

    [Fact]
    public async Task SetDnsOverride_updates_property_and_raises_Changed()
    {
        await using var manager = new SessionManager();
        var session = manager.GetOrStart(LoopbackNic());
        bool changed = false;
        session.Changed += () => changed = true;
        session.SetDnsOverride(IPAddress.Parse("1.1.1.1"));
        Assert.Equal(IPAddress.Parse("1.1.1.1"), session.DnsOverride);
        Assert.True(changed);
        session.SetDnsOverride(null);
        Assert.Null(session.DnsOverride);
    }

    [Fact]
    public async Task AddLaunchedProcess_tracks_pids()
    {
        await using var manager = new SessionManager();
        var session = manager.GetOrStart(LoopbackNic());
        session.AddLaunchedProcess(1234);
        session.AddLaunchedProcess(5678);
        Assert.Equal(new[] { 1234, 5678 }, session.LaunchedProcessIds);
    }

    [Fact]
    public async Task Failed_connection_sets_LastError()
    {
        await using var manager = new SessionManager();
        // NIC with no DNS servers: resolving any hostname fails -> LastError set.
        var session = manager.GetOrStart(LoopbackNic());
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, session.Port);
        var stream = client.GetStream();
        await stream.WriteAsync("CONNECT nosuch.example:443 HTTP/1.1\r\n\r\n"u8.ToArray());
        await RawSocket.ReadHeadAsync(stream);
        Assert.NotNull(session.LastError);
    }
}
