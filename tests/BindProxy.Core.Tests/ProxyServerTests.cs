using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Proxy;
using Xunit;

namespace BindProxy.Core.Tests;

public class ProxyServerTests
{
    private static ProxyServer StartProxy(Func<string, IPAddress>? resolve = null, TimeSpan? connectTimeout = null)
    {
        var proxy = new ProxyServer(IPAddress.Loopback, new StubResolver(resolve ?? IPAddress.Parse), connectTimeout);
        proxy.Start();
        return proxy;
    }

    [Fact]
    public async Task Forwards_plain_http_and_rewrites_to_origin_form()
    {
        using var origin = new FakeOriginServer();
        await using var proxy = StartProxy();
        using var http = new HttpClient(new HttpClientHandler
        {
            Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
        });
        var body = await http.GetStringAsync($"http://127.0.0.1:{origin.Port}/hello");
        Assert.Equal("hello", body);
        Assert.NotNull(origin.LastRequestHead);
        Assert.StartsWith("GET /hello HTTP/1.1\r\n", origin.LastRequestHead);
        Assert.Contains("Connection: close\r\n", origin.LastRequestHead);
        Assert.DoesNotContain("Proxy-Connection", origin.LastRequestHead);
    }

    [Fact]
    public async Task Forwards_plain_http_for_hostname_via_resolver()
    {
        using var origin = new FakeOriginServer();
        await using var proxy = StartProxy(host => host == "test.local"
            ? IPAddress.Loopback
            : throw new InvalidOperationException($"unexpected host {host}"));
        using var http = new HttpClient(new HttpClientHandler
        {
            Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
        });
        var body = await http.GetStringAsync($"http://test.local:{origin.Port}/x");
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task Connect_establishes_a_raw_tunnel()
    {
        // Raw echo origin: reads 4 bytes, answers "pong".
        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            using var c = await echo.AcceptTcpClientAsync();
            var s = c.GetStream();
            var b = new byte[4];
            await s.ReadExactlyAsync(b);
            await s.WriteAsync("pong"u8.ToArray());
        });

        await using var proxy = StartProxy();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"CONNECT 127.0.0.1:{echoPort} HTTP/1.1\r\nHost: 127.0.0.1:{echoPort}\r\n\r\n"));
        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 200", response);

        await stream.WriteAsync("ping"u8.ToArray());
        var buf = new byte[4];
        await stream.ReadExactlyAsync(buf);
        Assert.Equal("pong", Encoding.ASCII.GetString(buf));
        echo.Stop();
    }

    [Fact]
    public async Task Connect_to_dead_port_returns_502()
    {
        // Grab a port that nothing listens on.
        var placeholder = new TcpListener(IPAddress.Loopback, 0);
        placeholder.Start();
        int deadPort = ((IPEndPoint)placeholder.LocalEndpoint).Port;
        placeholder.Stop();

        await using var proxy = StartProxy();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{deadPort} HTTP/1.1\r\n\r\n"));
        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 502", response);
    }

    [Fact]
    public async Task Plain_http_forwards_request_body_after_header_rewrite()
    {
        using var origin = new BodyCaptureOriginServer();
        await using var proxy = StartProxy();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        var request =
            $"POST http://127.0.0.1:{origin.Port}/submit HTTP/1.1\r\n" +
            $"Host: 127.0.0.1:{origin.Port}\r\n" +
            "Content-Length: 5\r\n" +
            "Proxy-Connection: keep-alive\r\n\r\n" +
            "hello";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        var response = await RawSocket.ReadHeadAsync(stream);

        Assert.StartsWith("HTTP/1.1 200", response);
        Assert.NotNull(origin.LastRequestHead);
        Assert.StartsWith("POST /submit HTTP/1.1\r\n", origin.LastRequestHead);
        Assert.DoesNotContain("Proxy-Connection", origin.LastRequestHead);
        Assert.Equal("hello", origin.LastRequestBody);
    }

    [Fact]
    public async Task Dns_failure_returns_502_and_raises_error_event()
    {
        await using var proxy = StartProxy(_ => throw new InvalidOperationException("dns down"));
        string? reportedError = null;
        proxy.ConnectionError += msg => reportedError = msg;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync("CONNECT nosuch.example:443 HTTP/1.1\r\n\r\n"u8.ToArray());
        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 502", response);
        Assert.NotNull(reportedError);
        Assert.Contains("dns down", reportedError);
    }

    [Fact]
    public async Task Bad_request_raises_error_event()
    {
        await using var proxy = StartProxy();
        string? reportedError = null;
        proxy.ConnectionError += msg => reportedError = msg;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync("GET / HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray());

        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 400", response);
        Assert.NotNull(reportedError);
        Assert.Contains("bad request:", reportedError);
    }

    [Fact]
    public async Task Bad_request_returns_400()
    {
        await using var proxy = StartProxy();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        await stream.WriteAsync("GET / HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray());
        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 400", response);
    }

    [Fact]
    public async Task Oversized_request_head_returns_431()
    {
        await using var proxy = StartProxy();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        var hugeHeader = "X-Fill: " + new string('a', 33 * 1024);
        var request = $"CONNECT example.com:443 HTTP/1.1\r\n{hugeHeader}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 431", response);
    }

    [Fact]
    public async Task Oversized_request_head_raises_error_event()
    {
        await using var proxy = StartProxy();
        string? reportedError = null;
        proxy.ConnectionError += msg => reportedError = msg;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
        var stream = client.GetStream();
        var hugeHeader = "X-Fill: " + new string('a', 33 * 1024);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT example.com:443 HTTP/1.1\r\n{hugeHeader}"));

        var response = await RawSocket.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 431", response);
        Assert.Equal("request head exceeded 32 KiB", reportedError);
    }

    [Fact]
    public async Task Proxy_survives_client_disconnect_mid_header()
    {
        await using var proxy = StartProxy();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var stream = client.GetStream();
            await stream.WriteAsync("CONNECT example.com:443 HTTP/1.1\r\nHost: exa"u8.ToArray());
        }

        using var origin = new FakeOriginServer();
        using var http = new HttpClient(new HttpClientHandler
        {
            Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
        });
        var body = await http.GetStringAsync($"http://127.0.0.1:{origin.Port}/after-disconnect");
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task Proxy_survives_bad_request_and_serves_next_request()
    {
        await using var proxy = StartProxy();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var stream = client.GetStream();
            await stream.WriteAsync("GET / HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray());
            await RawSocket.ReadHeadAsync(stream);
        }

        using var origin = new FakeOriginServer();
        using var http = new HttpClient(new HttpClientHandler
        {
            Proxy = new FixedProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
        });
        var body = await http.GetStringAsync($"http://127.0.0.1:{origin.Port}/after-bad-request");
        Assert.Equal("hello", body);
    }

    [Fact]
    public async Task Dispose_stops_listening()
    {
        var proxy = StartProxy();
        int port = proxy.Port;
        await proxy.DisposeAsync();
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public void DisableNagle_sets_no_delay_on_a_real_socket()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Assert.False(socket.NoDelay);
        ProxyConnection.DisableNagle(socket);
        Assert.True(socket.NoDelay);
    }

    [Fact]
    public async Task Connect_tunnel_disables_nagle_on_the_accepted_client_socket()
    {
        // Stand-in for ProxyServer's listener, so we hold the actual TcpClient object
        // ProxyConnection.RunAsync operates on and can inspect its socket options afterward.
        var frontend = new TcpListener(IPAddress.Loopback, 0);
        frontend.Start();
        var acceptTask = frontend.AcceptTcpClientAsync();
        using var dialer = new TcpClient();
        await dialer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)frontend.LocalEndpoint).Port);
        using var accepted = await acceptTask;
        frontend.Stop();

        var echo = new TcpListener(IPAddress.Loopback, 0);
        echo.Start();
        int echoPort = ((IPEndPoint)echo.LocalEndpoint).Port;
        _ = Task.Run(async () => { using var c = await echo.AcceptTcpClientAsync(); });

        var runTask = ProxyConnection.RunAsync(
            accepted, IPAddress.Loopback, new StubResolver(IPAddress.Parse),
            TimeSpan.FromSeconds(5), _ => { }, () => { }, CancellationToken.None);

        var dialerStream = dialer.GetStream();
        await dialerStream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{echoPort} HTTP/1.1\r\n\r\n"));
        var response = await RawSocket.ReadHeadAsync(dialerStream);
        Assert.StartsWith("HTTP/1.1 200", response);

        Assert.True(accepted.Client.NoDelay);

        dialer.Dispose();
        await runTask;
        echo.Stop();
    }
}
