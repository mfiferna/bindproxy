using BindProxy.Core.Proxy;
using Xunit;

namespace BindProxy.Core.Tests;

public class HttpProxyRequestTests
{
    [Fact]
    public void Parses_connect_request()
    {
        var head = "CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443";
        Assert.True(HttpProxyRequest.TryParse(head, out var req, out _));
        Assert.Equal(ProxyRequestKind.Connect, req!.Kind);
        Assert.Equal("example.com", req.Host);
        Assert.Equal(443, req.Port);
        Assert.Equal("", req.RewrittenHead);
    }

    [Fact]
    public void Rewrites_absolute_http_request_to_origin_form()
    {
        var head = "GET http://example.com/a/b?q=1 HTTP/1.1\r\nHost: example.com\r\nProxy-Connection: keep-alive\r\nAccept: */*";
        Assert.True(HttpProxyRequest.TryParse(head, out var req, out _));
        Assert.Equal(ProxyRequestKind.Http, req!.Kind);
        Assert.Equal("example.com", req.Host);
        Assert.Equal(80, req.Port);
        Assert.StartsWith("GET /a/b?q=1 HTTP/1.1\r\n", req.RewrittenHead);
        Assert.Contains("Accept: */*\r\n", req.RewrittenHead);
        Assert.DoesNotContain("Proxy-Connection", req.RewrittenHead);
        Assert.Contains("Connection: close\r\n", req.RewrittenHead);
        Assert.EndsWith("\r\n\r\n", req.RewrittenHead);
    }

    [Fact]
    public void Uses_explicit_port_and_defaults_empty_path_to_slash()
    {
        var head = "GET http://example.com:8080 HTTP/1.1\r\nHost: example.com:8080";
        Assert.True(HttpProxyRequest.TryParse(head, out var req, out _));
        Assert.Equal(8080, req!.Port);
        Assert.StartsWith("GET / HTTP/1.1\r\n", req.RewrittenHead);
    }

    [Fact]
    public void Replaces_existing_connection_header_with_close()
    {
        var head = "GET http://example.com/ HTTP/1.1\r\nHost: example.com\r\nConnection: keep-alive";
        Assert.True(HttpProxyRequest.TryParse(head, out var req, out _));
        var occurrences = req!.RewrittenHead.Split("Connection:").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("Connection: close\r\n", req.RewrittenHead);
    }

    [Fact]
    public void Rejects_origin_form_request()
    {
        Assert.False(HttpProxyRequest.TryParse("GET / HTTP/1.1\r\nHost: x", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.False(HttpProxyRequest.TryParse("garbage", out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_ipv6_connect_target()
    {
        Assert.False(HttpProxyRequest.TryParse("CONNECT [::1]:443 HTTP/1.1", out _, out var error));
        Assert.NotNull(error);
    }
}
