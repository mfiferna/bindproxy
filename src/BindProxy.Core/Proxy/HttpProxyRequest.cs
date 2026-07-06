using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BindProxy.Core.Proxy;

public enum ProxyRequestKind { Connect, Http }

/// <summary>
/// A parsed proxy request head. For <see cref="ProxyRequestKind.Http"/>, <see cref="RewrittenHead"/>
/// is the complete head to send to the origin: origin-form request line, Proxy-Connection/Connection
/// stripped, "Connection: close" appended, terminated by a blank line. Empty for Connect.
/// </summary>
public sealed record HttpProxyRequest(ProxyRequestKind Kind, string Host, int Port, string RewrittenHead)
{
    public static bool TryParse(string head, [NotNullWhen(true)] out HttpProxyRequest? request, [NotNullWhen(false)] out string? error)
    {
        request = null;
        error = null;
        string[] lines = head.Split("\r\n");
        string[] parts = lines[0].Split(' ');
        if (parts.Length != 3)
        {
            error = $"Malformed request line: '{lines[0]}'";
            return false;
        }
        string method = parts[0], target = parts[1], version = parts[2];

        if (method == "CONNECT")
        {
            int colon = target.LastIndexOf(':');
            if (target.StartsWith('[') || colon <= 0 || !int.TryParse(target[(colon + 1)..], out int connectPort))
            {
                error = $"Unsupported CONNECT target: '{target}'";
                return false;
            }
            request = new HttpProxyRequest(ProxyRequestKind.Connect, target[..colon], connectPort, "");
            return true;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
        {
            error = $"Expected an absolute http:// URI, got '{target}'";
            return false;
        }

        var sb = new StringBuilder();
        sb.Append(method).Append(' ')
          .Append(uri.PathAndQuery.Length == 0 ? "/" : uri.PathAndQuery)
          .Append(' ').Append(version).Append("\r\n");
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) continue;
            sb.Append(line).Append("\r\n");
        }
        // One request per upstream connection: the origin closes after responding, which ends the
        // tunnel. Browsers reuse a proxy connection for *different* hosts, which raw tunneling
        // cannot support, so keep-alive must not survive past the first request.
        sb.Append("Connection: close\r\n\r\n");
        request = new HttpProxyRequest(ProxyRequestKind.Http, uri.Host, uri.Port, sb.ToString());
        return true;
    }
}
