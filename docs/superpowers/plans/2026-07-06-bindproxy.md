# BindProxy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows TUI app that runs a local HTTP proxy per NIC (outbound sockets bound to that NIC) and launches Chromium browsers through it.

**Architecture:** `BindProxy.Core` (class library: NIC enumeration, hand-rolled DNS over the NIC, HTTP CONNECT proxy, per-NIC sessions, browser detection/launch) + `BindProxy.Tui` (Terminal.Gui v2 front-end, Native AOT). Spec: `docs/superpowers/specs/2026-07-06-bindproxy-design.md`.

**Tech Stack:** .NET 10 (`net10.0-windows`), Terminal.Gui 2.4.16, xUnit 2.9.2, Native AOT.

## Global Constraints

- TargetFramework: `net10.0-windows` — set once in `Directory.Build.props`. If `dotnet --list-sdks` shows no 10.x SDK, use `net9.0-windows` instead (change only the props file).
- `TreatWarningsAsErrors=true`, `Nullable=enable` everywhere (trim/AOT analyzer warnings must fail the build).
- `BindProxy.Core`: `IsAotCompatible=true`, zero UI dependencies, no reflection-based serialization, no `dynamic`.
- `BindProxy.Tui`: `PublishAot=true`, only dependency is Terminal.Gui **2.4.16** (pin exactly; if restore fails, use latest 2.4.x and note it in the commit message).
- Windows-only. IPv4 only (v1). The proxy listens on `127.0.0.1` only.
- Every outbound socket (TCP to origins, UDP for DNS) must be `Bind()`-ed to the NIC's IPv4 address before connecting — this is the core mechanism; never skip the bind.
- Commit after every task with the message given in the task.

## File Structure

```
BindProxy.sln
Directory.Build.props
.gitignore
src/BindProxy.Core/BindProxy.Core.csproj
src/BindProxy.Core/Nics/NicInfo.cs            record: id, name, description, IPv4, DNS servers
src/BindProxy.Core/Nics/NicCatalog.cs         enumerate usable NICs
src/BindProxy.Core/Dns/DnsMessage.cs          build/parse DNS packets (pure)
src/BindProxy.Core/Dns/DnsExceptions.cs       DnsException, DnsResolutionException
src/BindProxy.Core/Dns/IDnsResolver.cs
src/BindProxy.Core/Dns/DnsResolver.cs         UDP queries bound to NIC + TTL cache
src/BindProxy.Core/Dns/SwappableResolver.cs   hot-swap resolver on DNS override change
src/BindProxy.Core/Proxy/HttpProxyRequest.cs  parse CONNECT / absolute-form head (pure)
src/BindProxy.Core/Proxy/ProxyConnection.cs   one client connection: resolve, bind, tunnel
src/BindProxy.Core/Proxy/ProxyServer.cs       listener + accept loop + connection counter
src/BindProxy.Core/Browsers/BrowserInfo.cs
src/BindProxy.Core/Browsers/IRegistryReader.cs
src/BindProxy.Core/Browsers/WindowsRegistryReader.cs
src/BindProxy.Core/Browsers/BrowserCatalog.cs registry scan for Chromium browsers
src/BindProxy.Core/Launch/ProfileMode.cs
src/BindProxy.Core/Launch/BrowserLauncher.cs  build args + Process.Start
src/BindProxy.Core/Sessions/ProxySession.cs   per-NIC proxy + DNS + PIDs + status events
src/BindProxy.Core/Sessions/SessionManager.cs per-NIC session registry
src/BindProxy.Tui/BindProxy.Tui.csproj
src/BindProxy.Tui/Program.cs
src/BindProxy.Tui/MainWindow.cs               stack of NIC rows
src/BindProxy.Tui/NicRowView.cs               one NIC: info left, buttons right
src/BindProxy.Tui/DnsDialog.cs                DNS override input dialog
tests/BindProxy.Core.Tests/BindProxy.Core.Tests.csproj
tests/BindProxy.Core.Tests/*.cs               one test class per component + fakes
README.md
```

Task order matters: 2→5 build the proxy pipeline bottom-up; 9 composes 4+5+6; 10 composes everything.

---

### Task 1: Solution scaffold

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `BindProxy.sln`, `src/BindProxy.Core/BindProxy.Core.csproj`, `src/BindProxy.Tui/BindProxy.Tui.csproj`, `src/BindProxy.Tui/Program.cs`, `tests/BindProxy.Core.Tests/BindProxy.Core.Tests.csproj`, `tests/BindProxy.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: buildable solution; all later tasks add files to these projects.

- [ ] **Step 1: Check the SDK version**

Run: `dotnet --list-sdks`
If a `10.x` SDK is listed, use `net10.0-windows` below. If only `9.x`, use `net9.0-windows` in `Directory.Build.props` (nowhere else).

- [ ] **Step 2: Write the root files**

`.gitignore`:

```
bin/
obj/
.vs/
*.user
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the three csproj files and a placeholder Program**

`src/BindProxy.Core/BindProxy.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAotCompatible>true</IsAotCompatible>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="BindProxy.Core.Tests" />
  </ItemGroup>
</Project>
```

`src/BindProxy.Tui/BindProxy.Tui.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Terminal.Gui" Version="2.4.16" />
    <ProjectReference Include="..\BindProxy.Core\BindProxy.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/BindProxy.Tui/Program.cs` (placeholder, replaced in Task 10):

```csharp
namespace BindProxy.Tui;

public static class Program
{
    public static void Main() => Console.WriteLine("BindProxy TUI placeholder");
}
```

`tests/BindProxy.Core.Tests/BindProxy.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\BindProxy.Core\BindProxy.Core.csproj" />
  </ItemGroup>
</Project>
```

`tests/BindProxy.Core.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace BindProxy.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Solution_builds_and_tests_run() => Assert.Equal(4, 2 + 2);
}
```

- [ ] **Step 4: Create the solution and add projects**

Run:

```bash
dotnet new sln -n BindProxy
dotnet sln add src/BindProxy.Core src/BindProxy.Tui tests/BindProxy.Core.Tests
```

- [ ] **Step 5: Build and test**

Run: `dotnet build && dotnet test tests/BindProxy.Core.Tests`
Expected: build succeeds (Terminal.Gui restores), 1 test passes. If Terminal.Gui 2.4.16 does not restore, run `dotnet add src/BindProxy.Tui package Terminal.Gui` to get the latest 2.4.x and note the version in the commit message.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold BindProxy solution (Core, Tui, tests)"
```

---

### Task 2: HTTP proxy request parsing

**Files:**
- Create: `src/BindProxy.Core/Proxy/HttpProxyRequest.cs`
- Test: `tests/BindProxy.Core.Tests/HttpProxyRequestTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum ProxyRequestKind { Connect, Http }`; `record HttpProxyRequest(ProxyRequestKind Kind, string Host, int Port, string RewrittenHead)` with `static bool TryParse(string head, out HttpProxyRequest? request, out string? error)`. `head` is the request head **without** the terminating blank line. `RewrittenHead` (Http only) ends with `\r\n\r\n` and is sent verbatim to the origin.

- [ ] **Step 1: Write the failing tests**

`tests/BindProxy.Core.Tests/HttpProxyRequestTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS with "The type or namespace name 'HttpProxyRequest' could not be found" (that is the failing state for a new type).

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Proxy/HttpProxyRequest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BindProxy.Core.Proxy;

public enum ProxyRequestKind { Connect, Http }

/// <summary>
/// A parsed proxy request head. For <see cref="ProxyRequestKind.Http"/>, <paramref name="RewrittenHead"/>
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Proxy/HttpProxyRequest.cs tests/BindProxy.Core.Tests/HttpProxyRequestTests.cs
git commit -m "feat: parse CONNECT and absolute-form HTTP proxy requests"
```

---

### Task 3: DNS message encoding/decoding

**Files:**
- Create: `src/BindProxy.Core/Dns/DnsMessage.cs`, `src/BindProxy.Core/Dns/DnsExceptions.cs`
- Test: `tests/BindProxy.Core.Tests/DnsMessageTests.cs`, `tests/BindProxy.Core.Tests/DnsTestData.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static byte[] DnsMessage.BuildQuery(ushort id, string hostname)`; `static DnsAnswer DnsMessage.Parse(ReadOnlySpan<byte> response, ushort expectedId)`; `record DnsAnswer(IReadOnlyList<IPAddress> Addresses, TimeSpan Ttl)`; `DnsException : Exception`. Test helper `DnsTestData.BuildResponse(byte[] query, IPAddress answer, int ttlSeconds)` reused by Task 4's fake server.

- [ ] **Step 1: Write the failing tests**

`tests/BindProxy.Core.Tests/DnsTestData.cs`:

```csharp
using System.Net;

namespace BindProxy.Core.Tests;

internal static class DnsTestData
{
    /// <summary>Builds a valid DNS response to <paramref name="query"/> with one A record.</summary>
    public static byte[] BuildResponse(byte[] query, IPAddress answer, int ttlSeconds)
    {
        var buf = new List<byte>(query.Length + 16)
        {
            query[0], query[1],   // same transaction id
            0x81, 0x80,           // QR=1 (response), RD, RA, rcode 0
            0x00, 0x01,           // QDCOUNT 1
            0x00, 0x01,           // ANCOUNT 1
            0x00, 0x00, 0x00, 0x00,
        };
        buf.AddRange(query[12..]);            // echo the question section
        buf.AddRange([0xC0, 0x0C]);           // answer name: pointer to offset 12
        buf.AddRange([0x00, 0x01, 0x00, 0x01]); // type A, class IN
        buf.AddRange([(byte)(ttlSeconds >> 24), (byte)(ttlSeconds >> 16), (byte)(ttlSeconds >> 8), (byte)ttlSeconds]);
        buf.AddRange([0x00, 0x04]);           // RDLENGTH 4
        buf.AddRange(answer.GetAddressBytes());
        return buf.ToArray();
    }
}
```

`tests/BindProxy.Core.Tests/DnsMessageTests.cs`:

```csharp
using System.Net;
using BindProxy.Core.Dns;
using Xunit;

namespace BindProxy.Core.Tests;

public class DnsMessageTests
{
    [Fact]
    public void Builds_a_query_for_hostname()
    {
        var query = DnsMessage.BuildQuery(0x2A, "example.com");
        byte[] expected =
        [
            0x00, 0x2A,             // id
            0x01, 0x00,             // flags: RD
            0x00, 0x01,             // QDCOUNT 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            3, (byte)'c', (byte)'o', (byte)'m',
            0,
            0x00, 0x01,             // QTYPE A
            0x00, 0x01,             // QCLASS IN
        ];
        Assert.Equal(expected, query);
    }

    [Fact]
    public void Rejects_invalid_hostname_label()
    {
        Assert.Throws<ArgumentException>(() => DnsMessage.BuildQuery(1, "bad..host"));
    }

    [Fact]
    public void Parses_response_with_compressed_name()
    {
        var query = DnsMessage.BuildQuery(0x2A, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Parse("93.184.216.34"), 60);
        var answer = DnsMessage.Parse(response, 0x2A);
        Assert.Equal(new[] { IPAddress.Parse("93.184.216.34") }, answer.Addresses);
        Assert.Equal(TimeSpan.FromSeconds(60), answer.Ttl);
    }

    [Fact]
    public void Parses_response_with_cname_before_a_record()
    {
        var query = DnsMessage.BuildQuery(7, "example.com");
        var buf = new List<byte>
        {
            0x00, 0x07, 0x81, 0x80,
            0x00, 0x01,             // QDCOUNT
            0x00, 0x02,             // ANCOUNT 2
            0x00, 0x00, 0x00, 0x00,
        };
        buf.AddRange(query[12..]);
        // CNAME record: name ptr, type 5, class 1, ttl 30, rdlength 2, rdata = pointer (lazy but legal)
        buf.AddRange([0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x1E, 0x00, 0x02, 0xC0, 0x0C]);
        // A record: name ptr, type 1, class 1, ttl 60, rdlength 4, 1.2.3.4
        buf.AddRange([0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 1, 2, 3, 4]);
        var answer = DnsMessage.Parse(buf.ToArray(), 7);
        Assert.Equal(new[] { IPAddress.Parse("1.2.3.4") }, answer.Addresses);
        Assert.Equal(TimeSpan.FromSeconds(60), answer.Ttl);
    }

    [Fact]
    public void Throws_on_id_mismatch()
    {
        var query = DnsMessage.BuildQuery(1, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Loopback, 60);
        Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 2));
    }

    [Fact]
    public void Throws_on_error_rcode()
    {
        var query = DnsMessage.BuildQuery(1, "example.com");
        var response = DnsTestData.BuildResponse(query, IPAddress.Loopback, 60);
        response[3] = (byte)(response[3] & 0xF0 | 0x03); // NXDOMAIN
        var ex = Assert.Throws<DnsException>(() => DnsMessage.Parse(response, 1));
        Assert.Contains("3", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'DnsMessage' could not be found").

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Dns/DnsExceptions.cs`:

```csharp
namespace BindProxy.Core.Dns;

/// <summary>A malformed or error DNS response.</summary>
public sealed class DnsException(string message) : Exception(message);

/// <summary>Resolution failed after trying all configured servers.</summary>
public sealed class DnsResolutionException(string host, string reason)
    : Exception($"DNS resolution failed for '{host}': {reason}")
{
    public string Host { get; } = host;
}
```

`src/BindProxy.Core/Dns/DnsMessage.cs`:

```csharp
using System.Net;
using System.Text;

namespace BindProxy.Core.Dns;

public sealed record DnsAnswer(IReadOnlyList<IPAddress> Addresses, TimeSpan Ttl);

/// <summary>Builds and parses the two DNS packet shapes this app needs: A-record query and response.</summary>
public static class DnsMessage
{
    public static byte[] BuildQuery(ushort id, string hostname)
    {
        var buf = new List<byte>(32)
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00,             // flags: recursion desired
            0x00, 0x01,             // QDCOUNT 1
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        foreach (var label in hostname.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException($"Invalid hostname label in '{hostname}'", nameof(hostname));
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }
        buf.Add(0x00);
        buf.AddRange([0x00, 0x01, 0x00, 0x01]); // QTYPE A, QCLASS IN
        return buf.ToArray();
    }

    public static DnsAnswer Parse(ReadOnlySpan<byte> response, ushort expectedId)
    {
        if (response.Length < 12) throw new DnsException("Response too short");
        ushort id = (ushort)(response[0] << 8 | response[1]);
        if (id != expectedId) throw new DnsException("Response transaction id mismatch");
        if ((response[2] & 0x80) == 0) throw new DnsException("Packet is not a response");
        int rcode = response[3] & 0x0F;
        if (rcode != 0) throw new DnsException($"Server returned error rcode {rcode}");
        int qdCount = response[4] << 8 | response[5];
        int anCount = response[6] << 8 | response[7];

        int pos = 12;
        for (int i = 0; i < qdCount; i++)
        {
            SkipName(response, ref pos);
            pos += 4; // QTYPE + QCLASS
        }

        var addresses = new List<IPAddress>();
        long minTtl = long.MaxValue;
        for (int i = 0; i < anCount; i++)
        {
            SkipName(response, ref pos);
            if (pos + 10 > response.Length) throw new DnsException("Truncated answer record");
            int type = response[pos] << 8 | response[pos + 1];
            int cls = response[pos + 2] << 8 | response[pos + 3];
            long ttl = (long)response[pos + 4] << 24 | (long)response[pos + 5] << 16
                     | (long)response[pos + 6] << 8 | response[pos + 7];
            int rdLength = response[pos + 8] << 8 | response[pos + 9];
            pos += 10;
            if (pos + rdLength > response.Length) throw new DnsException("Truncated answer rdata");
            if (type == 1 && cls == 1 && rdLength == 4)
            {
                addresses.Add(new IPAddress(response.Slice(pos, 4)));
                minTtl = Math.Min(minTtl, ttl);
            }
            pos += rdLength;
        }
        return new DnsAnswer(addresses, addresses.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(minTtl));
    }

    private static void SkipName(ReadOnlySpan<byte> data, ref int pos)
    {
        while (true)
        {
            if (pos >= data.Length) throw new DnsException("Truncated name");
            byte len = data[pos];
            if ((len & 0xC0) == 0xC0) { pos += 2; return; } // compression pointer ends the name
            if (len == 0) { pos += 1; return; }
            pos += len + 1;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Dns tests/BindProxy.Core.Tests/DnsMessageTests.cs tests/BindProxy.Core.Tests/DnsTestData.cs
git commit -m "feat: DNS A-record query builder and response parser"
```

---

### Task 4: DnsResolver — NIC-bound UDP queries, fallback, TTL cache

**Files:**
- Create: `src/BindProxy.Core/Dns/IDnsResolver.cs`, `src/BindProxy.Core/Dns/DnsResolver.cs`
- Test: `tests/BindProxy.Core.Tests/DnsResolverTests.cs`, `tests/BindProxy.Core.Tests/FakeDnsServer.cs`

**Interfaces:**
- Consumes: `DnsMessage`, `DnsException`, `DnsResolutionException`, `DnsTestData.BuildResponse` (Task 3).
- Produces: `interface IDnsResolver { Task<IPAddress> ResolveAsync(string host, CancellationToken ct); }`; `DnsResolver(IPAddress localAddress, IReadOnlyList<IPEndPoint> servers, TimeSpan? timeout = null)` implementing it, plus `void FlushCache()`.

- [ ] **Step 1: Write the fake DNS server test helper**

`tests/BindProxy.Core.Tests/FakeDnsServer.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace BindProxy.Core.Tests;

/// <summary>A loopback UDP server answering every A query with a fixed address.</summary>
internal sealed class FakeDnsServer : IDisposable
{
    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private readonly IPAddress _answer;
    private readonly int _ttlSeconds;
    private int _queryCount;

    public IPEndPoint EndPoint { get; }
    public int QueryCount => Volatile.Read(ref _queryCount);

    public FakeDnsServer(IPAddress answer, int ttlSeconds = 60)
    {
        _answer = answer;
        _ttlSeconds = ttlSeconds;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint = (IPEndPoint)_udp.Client.LocalEndPoint!;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var request = await _udp.ReceiveAsync(_cts.Token);
                Interlocked.Increment(ref _queryCount);
                var response = DnsTestData.BuildResponse(request.Buffer, _answer, _ttlSeconds);
                await _udp.SendAsync(response, request.RemoteEndPoint, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
        _cts.Dispose();
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/BindProxy.Core.Tests/DnsResolverTests.cs`:

```csharp
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
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'DnsResolver' could not be found").

- [ ] **Step 4: Write the implementation**

`src/BindProxy.Core/Dns/IDnsResolver.cs`:

```csharp
using System.Net;

namespace BindProxy.Core.Dns;

public interface IDnsResolver
{
    /// <summary>Resolves a hostname (or parses an IP literal) to a single IPv4 address.</summary>
    Task<IPAddress> ResolveAsync(string host, CancellationToken ct);
}
```

`src/BindProxy.Core/Dns/DnsResolver.cs`:

```csharp
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
        await udp.SendAsync(query, server, ct).ConfigureAwait(false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        var result = await udp.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
        var answer = DnsMessage.Parse(result.Buffer, id);
        if (answer.Addresses.Count == 0) throw new DnsException($"No A records for '{host}'");
        return (answer.Addresses[0], answer.Ttl);
    }

    public void FlushCache() => _cache.Clear();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS (the two timeout tests take ~250 ms each).

- [ ] **Step 6: Commit**

```bash
git add src/BindProxy.Core/Dns tests/BindProxy.Core.Tests/DnsResolverTests.cs tests/BindProxy.Core.Tests/FakeDnsServer.cs
git commit -m "feat: NIC-bound DNS resolver with server fallback and TTL cache"
```

---

### Task 5: ProxyServer — CONNECT tunneling and plain-HTTP forwarding

**Files:**
- Create: `src/BindProxy.Core/Proxy/ProxyConnection.cs`, `src/BindProxy.Core/Proxy/ProxyServer.cs`
- Test: `tests/BindProxy.Core.Tests/ProxyServerTests.cs`, `tests/BindProxy.Core.Tests/ProxyTestHelpers.cs`

**Interfaces:**
- Consumes: `HttpProxyRequest` (Task 2), `IDnsResolver`, `DnsResolutionException` (Task 4).
- Produces: `ProxyServer(IPAddress outboundAddress, IDnsResolver resolver)` with `void Start()`, `int Port`, `int ActiveConnections`, `event Action? ActiveConnectionsChanged`, `event Action<string>? ConnectionError`, `event Action? ConnectionSucceeded`, `ValueTask DisposeAsync()`. Listens on `127.0.0.1:<ephemeral>`; binds every outbound socket to `outboundAddress`.

- [ ] **Step 1: Write the test helpers**

`tests/BindProxy.Core.Tests/ProxyTestHelpers.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Tests;

/// <summary>IDnsResolver stub driven by a lambda.</summary>
internal sealed class StubResolver(Func<string, IPAddress> resolve) : IDnsResolver
{
    public Task<IPAddress> ResolveAsync(string host, CancellationToken ct) => Task.FromResult(resolve(host));
}

/// <summary>
/// IWebProxy that always proxies. .NET's WebProxy silently bypasses proxies for loopback
/// destinations, which would make these tests dial the origin directly.
/// </summary>
internal sealed class FixedProxy(Uri proxyUri) : IWebProxy
{
    public ICredentials? Credentials { get; set; }
    public Uri GetProxy(Uri destination) => proxyUri;
    public bool IsBypassed(Uri host) => false;
}

/// <summary>Minimal HTTP origin: captures the request head, answers "hello", closes.</summary>
internal sealed class FakeOriginServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }
    public volatile string? LastRequestHead;

    public FakeOriginServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var buffer = new byte[8192];
            while (!head.ToString().Contains("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(buffer, _cts.Token);
                if (read == 0) return;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            LastRequestHead = head.ToString();
            var response = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"u8.ToArray();
            await stream.WriteAsync(response, _cts.Token);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}

internal static class RawSocket
{
    /// <summary>Reads from the stream until the header terminator, returns the head text.</summary>
    public static async Task<string> ReadHeadAsync(NetworkStream stream)
    {
        var head = new StringBuilder();
        var buffer = new byte[1024];
        while (!head.ToString().Contains("\r\n\r\n"))
        {
            int read = await stream.ReadAsync(buffer);
            if (read == 0) break;
            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return head.ToString();
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/BindProxy.Core.Tests/ProxyServerTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Proxy;
using Xunit;

namespace BindProxy.Core.Tests;

public class ProxyServerTests
{
    private static ProxyServer StartProxy(Func<string, IPAddress>? resolve = null)
    {
        var proxy = new ProxyServer(IPAddress.Loopback, new StubResolver(resolve ?? IPAddress.Parse));
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
    public async Task Dispose_stops_listening()
    {
        var proxy = StartProxy();
        int port = proxy.Port;
        await proxy.DisposeAsync();
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(IPAddress.Loopback, port));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'ProxyServer' could not be found").

- [ ] **Step 4: Write the implementation**

`src/BindProxy.Core/Proxy/ProxyConnection.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Proxy;

/// <summary>Handles one accepted client connection end-to-end.</summary>
internal static class ProxyConnection
{
    private const int MaxHeadBytes = 32 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public static async Task RunAsync(
        TcpClient client,
        IPAddress outboundAddress,
        IDnsResolver resolver,
        Action<string> onError,
        Action onSuccess,
        CancellationToken ct)
    {
        var clientStream = client.GetStream();
        var (head, leftover) = await ReadHeadAsync(clientStream, ct).ConfigureAwait(false);
        if (head is null) return; // client hung up or head too large

        if (!HttpProxyRequest.TryParse(head, out var request, out var parseError))
        {
            await WriteErrorAsync(clientStream, "400 Bad Request", parseError, ct).ConfigureAwait(false);
            return;
        }

        IPAddress remoteAddress;
        try
        {
            remoteAddress = await resolver.ResolveAsync(request.Host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onError($"DNS: {ex.Message}");
            await WriteErrorAsync(clientStream, "502 Bad Gateway", $"DNS resolution failed: {ex.Message}", ct).ConfigureAwait(false);
            return;
        }

        using var server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // The bind below is the entire point of this app: it pins the outbound
            // connection to the selected NIC.
            server.Bind(new IPEndPoint(outboundAddress, 0));
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);
            await server.ConnectAsync(new IPEndPoint(remoteAddress, request.Port), connectCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            onError($"connect timeout: {request.Host}:{request.Port}");
            await WriteErrorAsync(clientStream, "504 Gateway Timeout", $"Connect to {request.Host}:{request.Port} timed out", ct).ConfigureAwait(false);
            return;
        }
        catch (SocketException ex)
        {
            onError($"connect {request.Host}:{request.Port}: {ex.SocketErrorCode}");
            await WriteErrorAsync(clientStream, "502 Bad Gateway", $"Connect failed: {ex.SocketErrorCode}", ct).ConfigureAwait(false);
            return;
        }

        onSuccess();
        await using var serverStream = new NetworkStream(server, ownsSocket: false);
        if (request.Kind == ProxyRequestKind.Connect)
        {
            await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct).ConfigureAwait(false);
        }
        else
        {
            await serverStream.WriteAsync(Encoding.ASCII.GetBytes(request.RewrittenHead), ct).ConfigureAwait(false);
        }
        if (leftover.Length > 0)
        {
            await serverStream.WriteAsync(leftover, ct).ConfigureAwait(false);
        }
        await PumpAsync(clientStream, serverStream, ct).ConfigureAwait(false);
    }

    private static async Task<(string? Head, byte[] Leftover)> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeadBytes];
        int filled = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(filled), ct).ConfigureAwait(false);
            if (read == 0) return (null, []);
            filled += read;
            int end = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                string head = Encoding.ASCII.GetString(buffer, 0, end);
                byte[] leftover = buffer.AsSpan(end + 4, filled - end - 4).ToArray();
                return (head, leftover);
            }
            if (filled == buffer.Length) return (null, []);
        }
    }

    private static async Task PumpAsync(Stream a, Stream b, CancellationToken ct)
    {
        var ab = CopySilentlyAsync(a, b, ct);
        var ba = CopySilentlyAsync(b, a, ct);
        await Task.WhenAny(ab, ba).ConfigureAwait(false);
        // Closing both streams unblocks whichever direction is still reading.
        a.Dispose();
        b.Dispose();
        await Task.WhenAll(ab, ba).ConfigureAwait(false);
    }

    private static async Task CopySilentlyAsync(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, ct).ConfigureAwait(false); }
        catch { /* disconnects and cancellation end the pump; nothing to report */ }
    }

    private static async Task WriteErrorAsync(NetworkStream stream, string status, string? reason, CancellationToken ct)
    {
        string body = (reason ?? "") + "\n";
        string response = $"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        try { await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct).ConfigureAwait(false); }
        catch { /* client already gone */ }
    }
}
```

`src/BindProxy.Core/Proxy/ProxyServer.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using BindProxy.Core.Dns;

namespace BindProxy.Core.Proxy;

/// <summary>
/// An HTTP forward proxy on 127.0.0.1 whose outbound connections are bound to one NIC's address.
/// One instance per session/NIC.
/// </summary>
public sealed class ProxyServer(IPAddress outboundAddress, IDnsResolver resolver) : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private int _activeConnections;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int ActiveConnections => Volatile.Read(ref _activeConnections);

    public event Action? ActiveConnectionsChanged;
    /// <summary>Raised with a short message when a connection fails before its tunnel is established.</summary>
    public event Action<string>? ConnectionError;
    /// <summary>Raised when an outbound connection is established (used to clear error states).</summary>
    public event Action? ConnectionSucceeded;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        Interlocked.Increment(ref _activeConnections);
        ActiveConnectionsChanged?.Invoke();
        try
        {
            await ProxyConnection.RunAsync(
                client, outboundAddress, resolver,
                msg => ConnectionError?.Invoke(msg),
                () => ConnectionSucceeded?.Invoke(),
                _cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // A single connection must never take down the server.
        }
        finally
        {
            client.Dispose();
            Interlocked.Decrement(ref _activeConnections);
            ActiveConnectionsChanged?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BindProxy.Core/Proxy tests/BindProxy.Core.Tests/ProxyServerTests.cs tests/BindProxy.Core.Tests/ProxyTestHelpers.cs
git commit -m "feat: NIC-bound HTTP proxy server with CONNECT tunneling"
```

---

### Task 6: NIC enumeration

**Files:**
- Create: `src/BindProxy.Core/Nics/NicInfo.cs`, `src/BindProxy.Core/Nics/NicCatalog.cs`
- Test: `tests/BindProxy.Core.Tests/NicCatalogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record NicInfo(string Id, string Name, string Description, IPAddress Ipv4Address, IReadOnlyList<IPAddress> DnsServers)`; `static IReadOnlyList<NicInfo> NicCatalog.GetUsableNics()`.

- [ ] **Step 1: Write the failing tests**

These run against the real machine, so they assert invariants rather than specific NICs.

`tests/BindProxy.Core.Tests/NicCatalogTests.cs`:

```csharp
using System.Net.Sockets;
using BindProxy.Core.Nics;
using Xunit;

namespace BindProxy.Core.Tests;

public class NicCatalogTests
{
    [Fact]
    public void Returns_without_throwing()
    {
        var nics = NicCatalog.GetUsableNics();
        Assert.NotNull(nics);
    }

    [Fact]
    public void Every_nic_has_an_ipv4_address_and_id()
    {
        foreach (var nic in NicCatalog.GetUsableNics())
        {
            Assert.False(string.IsNullOrEmpty(nic.Id));
            Assert.False(string.IsNullOrEmpty(nic.Name));
            Assert.Equal(AddressFamily.InterNetwork, nic.Ipv4Address.AddressFamily);
            Assert.All(nic.DnsServers, s => Assert.Equal(AddressFamily.InterNetwork, s.AddressFamily));
        }
    }

    [Fact]
    public void Excludes_loopback()
    {
        Assert.DoesNotContain(NicCatalog.GetUsableNics(),
            n => n.Ipv4Address.Equals(System.Net.IPAddress.Loopback));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'NicCatalog' could not be found").

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Nics/NicInfo.cs`:

```csharp
using System.Net;

namespace BindProxy.Core.Nics;

/// <summary>A usable network interface: up, non-loopback, with an IPv4 address.</summary>
public sealed record NicInfo(
    string Id,
    string Name,
    string Description,
    IPAddress Ipv4Address,
    IReadOnlyList<IPAddress> DnsServers);
```

`src/BindProxy.Core/Nics/NicCatalog.cs`:

```csharp
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BindProxy.Core.Nics;

public static class NicCatalog
{
    public static IReadOnlyList<NicInfo> GetUsableNics()
    {
        var result = new List<NicInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            var ipv4 = props.UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null) continue;
            var dns = props.DnsAddresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToArray();
            result.Add(new NicInfo(nic.Id, nic.Name, nic.Description, ipv4, dns));
        }
        return result.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Nics tests/BindProxy.Core.Tests/NicCatalogTests.cs
git commit -m "feat: enumerate usable NICs with their DNS servers"
```

---

### Task 7: Browser detection from the registry

**Files:**
- Create: `src/BindProxy.Core/Browsers/BrowserInfo.cs`, `src/BindProxy.Core/Browsers/IRegistryReader.cs`, `src/BindProxy.Core/Browsers/WindowsRegistryReader.cs`, `src/BindProxy.Core/Browsers/BrowserCatalog.cs`
- Test: `tests/BindProxy.Core.Tests/BrowserCatalogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `record BrowserInfo(string Name, string ExePath)`; `interface IRegistryReader { IReadOnlyList<string> GetSubKeyNames(string keyPath); string? GetDefaultValue(string keyPath); }` (paths are full, e.g. `HKEY_LOCAL_MACHINE\SOFTWARE\...`); `BrowserCatalog(IRegistryReader registry)` with `IReadOnlyList<BrowserInfo> GetChromiumBrowsers()`; `WindowsRegistryReader : IRegistryReader`.

- [ ] **Step 1: Write the failing tests**

`tests/BindProxy.Core.Tests/BrowserCatalogTests.cs`:

```csharp
using BindProxy.Core.Browsers;
using Xunit;

namespace BindProxy.Core.Tests;

internal sealed class FakeRegistryReader : IRegistryReader
{
    public Dictionary<string, string[]> SubKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> DefaultValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetSubKeyNames(string keyPath) => SubKeys.GetValueOrDefault(keyPath, []);
    public string? GetDefaultValue(string keyPath) => DefaultValues.GetValueOrDefault(keyPath);
}

public class BrowserCatalogTests
{
    private const string Hklm = @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet";
    private const string Hkcu = @"HKEY_CURRENT_USER\SOFTWARE\Clients\StartMenuInternet";

    private static FakeRegistryReader RegistryWith(string basePath, string subKey, string? displayName, string command)
    {
        var reg = new FakeRegistryReader();
        reg.SubKeys[basePath] = [subKey];
        reg.DefaultValues[$@"{basePath}\{subKey}"] = displayName;
        reg.DefaultValues[$@"{basePath}\{subKey}\shell\open\command"] = command;
        return reg;
    }

    [Fact]
    public void Finds_chrome_with_quoted_command()
    {
        var reg = RegistryWith(Hklm, "Google Chrome", "Google Chrome",
            "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"");
        var browsers = new BrowserCatalog(reg).GetChromiumBrowsers();
        var b = Assert.Single(browsers);
        Assert.Equal("Google Chrome", b.Name);
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", b.ExePath);
    }

    [Fact]
    public void Finds_browser_with_unquoted_command()
    {
        var reg = RegistryWith(Hkcu, "Brave", "Brave",
            @"C:\Users\x\AppData\Local\BraveSoftware\Brave-Browser\Application\brave.exe");
        var b = Assert.Single(new BrowserCatalog(reg).GetChromiumBrowsers());
        Assert.EndsWith("brave.exe", b.ExePath);
    }

    [Fact]
    public void Skips_non_chromium_browsers()
    {
        var reg = RegistryWith(Hklm, "FIREFOX.EXE", "Mozilla Firefox",
            "\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\"");
        Assert.Empty(new BrowserCatalog(reg).GetChromiumBrowsers());
    }

    [Fact]
    public void Dedupes_same_exe_across_hives()
    {
        var reg = RegistryWith(Hklm, "Google Chrome", "Google Chrome",
            "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"");
        reg.SubKeys[Hkcu] = ["Google Chrome"];
        reg.DefaultValues[$@"{Hkcu}\Google Chrome"] = "Google Chrome";
        reg.DefaultValues[$@"{Hkcu}\Google Chrome\shell\open\command"] =
            "\"C:\\PROGRAM FILES\\GOOGLE\\CHROME\\APPLICATION\\CHROME.EXE\"";
        Assert.Single(new BrowserCatalog(reg).GetChromiumBrowsers());
    }

    [Fact]
    public void Falls_back_to_subkey_name_when_display_name_missing()
    {
        var reg = RegistryWith(Hklm, "Vivaldi", null,
            "\"C:\\Users\\x\\AppData\\Local\\Vivaldi\\Application\\vivaldi.exe\"");
        Assert.Equal("Vivaldi", Assert.Single(new BrowserCatalog(reg).GetChromiumBrowsers()).Name);
    }

    [Fact]
    public void Handles_missing_command_and_empty_registry()
    {
        var reg = new FakeRegistryReader();
        reg.SubKeys[Hklm] = ["Broken"];
        Assert.Empty(new BrowserCatalog(reg).GetChromiumBrowsers());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'IRegistryReader' could not be found").

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Browsers/BrowserInfo.cs`:

```csharp
namespace BindProxy.Core.Browsers;

public sealed record BrowserInfo(string Name, string ExePath);
```

`src/BindProxy.Core/Browsers/IRegistryReader.cs`:

```csharp
namespace BindProxy.Core.Browsers;

/// <summary>Registry access behind an interface so browser detection is testable.
/// Key paths are full, e.g. @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet".</summary>
public interface IRegistryReader
{
    IReadOnlyList<string> GetSubKeyNames(string keyPath);
    string? GetDefaultValue(string keyPath);
}
```

`src/BindProxy.Core/Browsers/WindowsRegistryReader.cs`:

```csharp
using Microsoft.Win32;

namespace BindProxy.Core.Browsers;

public sealed class WindowsRegistryReader : IRegistryReader
{
    public IReadOnlyList<string> GetSubKeyNames(string keyPath)
    {
        using var key = Open(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }

    public string? GetDefaultValue(string keyPath)
    {
        using var key = Open(keyPath);
        return key?.GetValue(null) as string;
    }

    private static RegistryKey? Open(string keyPath)
    {
        int separator = keyPath.IndexOf('\\');
        var hive = keyPath[..separator] switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Unknown hive in '{keyPath}'", nameof(keyPath)),
        };
        return hive.OpenSubKey(keyPath[(separator + 1)..]);
    }
}
```

`src/BindProxy.Core/Browsers/BrowserCatalog.cs`:

```csharp
namespace BindProxy.Core.Browsers;

/// <summary>Finds installed Chromium-family browsers via the StartMenuInternet registry keys.</summary>
public sealed class BrowserCatalog(IRegistryReader registry)
{
    private static readonly string[] BasePaths =
    [
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Clients\StartMenuInternet",
        @"HKEY_CURRENT_USER\SOFTWARE\Clients\StartMenuInternet",
    ];

    private static readonly HashSet<string> ChromiumExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe", "opera.exe", "chromium.exe",
    };

    public IReadOnlyList<BrowserInfo> GetChromiumBrowsers()
    {
        var byPath = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var basePath in BasePaths)
        {
            foreach (var subKey in registry.GetSubKeyNames(basePath))
            {
                var command = registry.GetDefaultValue($@"{basePath}\{subKey}\shell\open\command");
                var exePath = ExtractExePath(command);
                if (exePath is null || !ChromiumExeNames.Contains(Path.GetFileName(exePath))) continue;
                var name = registry.GetDefaultValue($@"{basePath}\{subKey}") ?? subKey;
                byPath.TryAdd(exePath, new BrowserInfo(name, exePath));
            }
        }
        return byPath.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int close = command.IndexOf('"', 1);
            return close > 1 ? command[1..close] : null;
        }
        return command;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Browsers tests/BindProxy.Core.Tests/BrowserCatalogTests.cs
git commit -m "feat: detect installed Chromium-family browsers via registry"
```

---

### Task 8: Browser launcher

**Files:**
- Create: `src/BindProxy.Core/Launch/ProfileMode.cs`, `src/BindProxy.Core/Launch/BrowserLauncher.cs`
- Test: `tests/BindProxy.Core.Tests/BrowserLauncherTests.cs`

**Interfaces:**
- Consumes: `BrowserInfo` (Task 7).
- Produces: `enum ProfileMode { Isolated, UserDefault }`; `static class BrowserLauncher` with `IReadOnlyList<string> BuildArguments(int proxyPort, string? profileDir)`, `string GetProfileDir(string browserName, string nicId)`, `int Launch(BrowserInfo browser, int proxyPort, string nicId, ProfileMode profileMode = ProfileMode.Isolated)` (returns the PID).

- [ ] **Step 1: Write the failing tests**

`tests/BindProxy.Core.Tests/BrowserLauncherTests.cs`:

```csharp
using BindProxy.Core.Launch;
using Xunit;

namespace BindProxy.Core.Tests;

public class BrowserLauncherTests
{
    [Fact]
    public void Builds_arguments_with_isolated_profile()
    {
        var args = BrowserLauncher.BuildArguments(8080, @"C:\profiles\chrome-nic1");
        Assert.Equal(new[]
        {
            "--proxy-server=http://127.0.0.1:8080",
            "--proxy-bypass-list=<-loopback>",
            @"--user-data-dir=C:\profiles\chrome-nic1",
            "--no-first-run",
            "--no-default-browser-check",
        }, args);
    }

    [Fact]
    public void Omits_user_data_dir_when_profile_dir_is_null()
    {
        var args = BrowserLauncher.BuildArguments(8080, null);
        Assert.DoesNotContain(args, a => a.StartsWith("--user-data-dir"));
        Assert.Contains("--proxy-server=http://127.0.0.1:8080", args);
    }

    [Fact]
    public void Profile_dir_is_sanitized_and_under_local_appdata()
    {
        var dir = BrowserLauncher.GetProfileDir("Google Chrome", "{B2AA02F3-FF44-4E52-A}");
        Assert.Contains(@"BindProxy\Profiles", dir);
        var leaf = Path.GetFileName(dir);
        Assert.Matches("^[A-Za-z0-9._-]+$", leaf);
        Assert.Contains("Google_Chrome", leaf);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'BrowserLauncher' could not be found").

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Launch/ProfileMode.cs`:

```csharp
namespace BindProxy.Core.Launch;

/// <summary>
/// Internal option, not exposed in the UI yet. UserDefault omits --user-data-dir; note that
/// Chromium silently ignores --proxy-server when an instance with that profile is already
/// running, so UserDefault only works when the browser is fully closed.
/// </summary>
public enum ProfileMode
{
    Isolated,
    UserDefault,
}
```

`src/BindProxy.Core/Launch/BrowserLauncher.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using BindProxy.Core.Browsers;

namespace BindProxy.Core.Launch;

public static class BrowserLauncher
{
    /// <summary>Starts the browser through the session's proxy. Returns the process id.</summary>
    public static int Launch(BrowserInfo browser, int proxyPort, string nicId, ProfileMode profileMode = ProfileMode.Isolated)
    {
        string? profileDir = profileMode == ProfileMode.Isolated ? GetProfileDir(browser.Name, nicId) : null;
        var psi = new ProcessStartInfo(browser.ExePath) { UseShellExecute = false };
        foreach (var arg in BuildArguments(proxyPort, profileDir))
        {
            psi.ArgumentList.Add(arg);
        }
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {browser.Name}");
        return process.Id;
    }

    public static IReadOnlyList<string> BuildArguments(int proxyPort, string? profileDir)
    {
        var args = new List<string>
        {
            $"--proxy-server=http://127.0.0.1:{proxyPort}",
            "--proxy-bypass-list=<-loopback>",
        };
        if (profileDir is not null)
        {
            args.Add($"--user-data-dir={profileDir}");
        }
        args.Add("--no-first-run");
        args.Add("--no-default-browser-check");
        return args;
    }

    /// <summary>Per-browser, per-NIC persistent profile directory.</summary>
    public static string GetProfileDir(string browserName, string nicId)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BindProxy", "Profiles", Sanitize($"{browserName}-{nicId}"));
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Launch tests/BindProxy.Core.Tests/BrowserLauncherTests.cs
git commit -m "feat: launch Chromium browsers through the session proxy"
```

---

### Task 9: Sessions — per-NIC proxy lifecycle

**Files:**
- Create: `src/BindProxy.Core/Dns/SwappableResolver.cs`, `src/BindProxy.Core/Sessions/ProxySession.cs`, `src/BindProxy.Core/Sessions/SessionManager.cs`
- Test: `tests/BindProxy.Core.Tests/SessionManagerTests.cs`

**Interfaces:**
- Consumes: `NicInfo` (Task 6), `IDnsResolver`, `DnsResolver` (Task 4), `ProxyServer` (Task 5).
- Produces:
  - `SwappableResolver(IDnsResolver initial) : IDnsResolver` with `void Swap(IDnsResolver next)`.
  - `ProxySession` (internal ctor): `NicInfo Nic`, `int Port`, `string ProxyUrl`, `IPAddress? DnsOverride`, `string? LastError`, `int ActiveConnections`, `IReadOnlyList<int> LaunchedProcessIds`, `event Action? Changed`, `void SetDnsOverride(IPAddress? server)`, `void AddLaunchedProcess(int pid)`, `ValueTask DisposeAsync()`.
  - `SessionManager : IAsyncDisposable`: `IReadOnlyList<ProxySession> Sessions`, `ProxySession? GetSession(string nicId)`, `ProxySession GetOrStart(NicInfo nic, IPAddress? dnsOverride = null)`, `Task StopAsync(string nicId)`, `event Action? SessionsChanged`.

- [ ] **Step 1: Write the failing tests**

`tests/BindProxy.Core.Tests/SessionManagerTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: build FAILS ("'SessionManager' could not be found").

- [ ] **Step 3: Write the implementation**

`src/BindProxy.Core/Dns/SwappableResolver.cs`:

```csharp
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
```

`src/BindProxy.Core/Sessions/ProxySession.cs`:

```csharp
using System.Net;
using BindProxy.Core.Dns;
using BindProxy.Core.Nics;
using BindProxy.Core.Proxy;

namespace BindProxy.Core.Sessions;

/// <summary>One NIC's running proxy plus its DNS setting and launched-browser bookkeeping.</summary>
public sealed class ProxySession : IAsyncDisposable
{
    private readonly SwappableResolver _resolver;
    private readonly ProxyServer _server;
    private readonly List<int> _pids = [];
    private readonly object _lock = new();

    public NicInfo Nic { get; }
    public IPAddress? DnsOverride { get; private set; }
    public string? LastError { get; private set; }
    public int Port => _server.Port;
    public string ProxyUrl => $"http://127.0.0.1:{Port}";
    public int ActiveConnections => _server.ActiveConnections;

    public IReadOnlyList<int> LaunchedProcessIds
    {
        get { lock (_lock) return _pids.ToArray(); }
    }

    /// <summary>Raised on any observable change: connections, DNS, PIDs, errors.</summary>
    public event Action? Changed;

    internal ProxySession(NicInfo nic, IPAddress? dnsOverride)
    {
        Nic = nic;
        DnsOverride = dnsOverride;
        _resolver = new SwappableResolver(BuildResolver(nic, dnsOverride));
        _server = new ProxyServer(nic.Ipv4Address, _resolver);
        _server.ActiveConnectionsChanged += () => Changed?.Invoke();
        _server.ConnectionError += message =>
        {
            LastError = message;
            Changed?.Invoke();
        };
        _server.ConnectionSucceeded += () =>
        {
            if (LastError is null) return;
            LastError = null;
            Changed?.Invoke();
        };
        _server.Start();
    }

    private static DnsResolver BuildResolver(NicInfo nic, IPAddress? dnsOverride)
    {
        var servers = dnsOverride is not null
            ? new[] { new IPEndPoint(dnsOverride, 53) }
            : nic.DnsServers.Select(s => new IPEndPoint(s, 53)).ToArray();
        return new DnsResolver(nic.Ipv4Address, servers);
    }

    public void SetDnsOverride(IPAddress? server)
    {
        DnsOverride = server;
        _resolver.Swap(BuildResolver(Nic, server));
        Changed?.Invoke();
    }

    public void AddLaunchedProcess(int pid)
    {
        lock (_lock) _pids.Add(pid);
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
```

`src/BindProxy.Core/Sessions/SessionManager.cs`:

```csharp
using System.Net;
using BindProxy.Core.Nics;

namespace BindProxy.Core.Sessions;

/// <summary>Registry of running sessions, keyed per NIC. Launching several browsers on one NIC
/// reuses that NIC's session.</summary>
public sealed class SessionManager : IAsyncDisposable
{
    private readonly Dictionary<string, ProxySession> _sessions = new();
    private readonly object _lock = new();

    /// <summary>Raised when a session starts or stops.</summary>
    public event Action? SessionsChanged;

    public IReadOnlyList<ProxySession> Sessions
    {
        get { lock (_lock) return _sessions.Values.ToArray(); }
    }

    public ProxySession? GetSession(string nicId)
    {
        lock (_lock) return _sessions.GetValueOrDefault(nicId);
    }

    public ProxySession GetOrStart(NicInfo nic, IPAddress? dnsOverride = null)
    {
        ProxySession session;
        lock (_lock)
        {
            if (_sessions.TryGetValue(nic.Id, out var existing)) return existing;
            session = new ProxySession(nic, dnsOverride);
            _sessions[nic.Id] = session;
        }
        SessionsChanged?.Invoke();
        return session;
    }

    public async Task StopAsync(string nicId)
    {
        ProxySession? session;
        lock (_lock)
        {
            if (!_sessions.Remove(nicId, out session)) return;
        }
        await session.DisposeAsync().ConfigureAwait(false);
        SessionsChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in Sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        lock (_lock) _sessions.Clear();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Core/Dns/SwappableResolver.cs src/BindProxy.Core/Sessions tests/BindProxy.Core.Tests/SessionManagerTests.cs
git commit -m "feat: per-NIC proxy sessions with DNS override and error tracking"
```

---

### Task 10: TUI — Terminal.Gui NIC rows

**Files:**
- Create: `src/BindProxy.Tui/MainWindow.cs`, `src/BindProxy.Tui/NicRowView.cs`, `src/BindProxy.Tui/DnsDialog.cs`
- Modify: `src/BindProxy.Tui/Program.cs` (replace the Task 1 placeholder)

**Interfaces:**
- Consumes: `NicCatalog`, `NicInfo` (Task 6), `BrowserCatalog`, `WindowsRegistryReader`, `BrowserInfo` (Task 7), `BrowserLauncher` (Task 8), `SessionManager`, `ProxySession` (Task 9).
- Produces: the runnable TUI. No Core changes allowed in this task.

> **Terminal.Gui 2.4 API note:** the exact namespaces and event names moved during v2 development. The code below targets 2.4.x with split namespaces (`Terminal.Gui.App`, `Terminal.Gui.ViewBase`, `Terminal.Gui.Views`, `Terminal.Gui.Drawing`, `Terminal.Gui.Input`). If the compiler cannot find these namespaces, replace them with a single `using Terminal.Gui;`. If `Accepting` does not exist on `Button`, the event is named `Accept` in that build; if `e.Handled` does not exist on its args, the property is `e.Cancel`. If `SetNeedsDraw()` does not exist, it is `SetNeedsDisplay()`. Make only these mechanical fixes — do not redesign the layout. This is UI glue over a fully tested Core, so there are no unit tests; verification is the manual smoke test in Step 4.

- [ ] **Step 1: Write the TUI files**

`src/BindProxy.Tui/Program.cs` (replace placeholder):

```csharp
using BindProxy.Core.Browsers;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Terminal.Gui.App;

namespace BindProxy.Tui;

public static class Program
{
    public static void Main()
    {
        var nics = NicCatalog.GetUsableNics();
        var browsers = new BrowserCatalog(new WindowsRegistryReader()).GetChromiumBrowsers();
        var sessions = new SessionManager();
        Application.Init();
        try
        {
            using var window = new MainWindow(nics, browsers, sessions);
            Application.Run(window);
        }
        finally
        {
            Application.Shutdown();
            // Quit tears down every proxy session.
            sessions.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
```

`src/BindProxy.Tui/MainWindow.cs`:

```csharp
using BindProxy.Core.Browsers;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BindProxy.Tui;

public sealed class MainWindow : Window
{
    public MainWindow(
        IReadOnlyList<NicInfo> nics,
        IReadOnlyList<BrowserInfo> browsers,
        SessionManager sessions)
    {
        Title = "BindProxy — pin a browser to a NIC (Esc quits, stops all proxies)";

        if (nics.Count == 0)
        {
            Add(new Label { X = 1, Y = 1, Text = "No usable network interfaces found." });
            return;
        }
        if (browsers.Count == 0)
        {
            Add(new Label { X = 1, Y = 0, Text = "No Chromium-family browsers found — Manual mode still works." });
        }

        int y = browsers.Count == 0 ? 1 : 0;
        foreach (var nic in nics)
        {
            var row = new NicRowView(nic, browsers, sessions) { Y = y };
            Add(row);
            y += 5;
        }
    }
}
```

`src/BindProxy.Tui/NicRowView.cs`:

```csharp
using System.Net;
using BindProxy.Core.Browsers;
using BindProxy.Core.Launch;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BindProxy.Tui;

/// <summary>One NIC: info + status on the left, launch/manual/DNS/stop buttons on the right.</summary>
public sealed class NicRowView : View
{
    private readonly NicInfo _nic;
    private readonly SessionManager _sessions;
    private readonly Label _statusLabel;
    private readonly Button _stopButton;
    private IPAddress? _pendingDnsOverride;
    private ProxySession? _hookedSession;

    public NicRowView(NicInfo nic, IReadOnlyList<BrowserInfo> browsers, SessionManager sessions)
    {
        _nic = nic;
        _sessions = sessions;
        Width = Dim.Fill();
        Height = 5;
        BorderStyle = LineStyle.Single;
        Title = nic.Name;

        var dnsText = nic.DnsServers.Count == 0 ? "none" : string.Join(", ", nic.DnsServers);
        Add(new Label { X = 0, Y = 0, Text = $"{nic.Ipv4Address}  {Truncate(nic.Description, 38)}" });
        Add(new Label { X = 0, Y = 1, Text = $"DNS: {Truncate(dnsText, 42)}" });
        _statusLabel = new Label { X = 0, Y = 2, Width = Dim.Fill(), Text = "stopped" };
        Add(_statusLabel);

        // Buttons, right half: browsers on the first line, actions on the second.
        View? previous = null;
        foreach (var browser in browsers)
        {
            var launchButton = new Button
            {
                Text = ShortName(browser.Name),
                Y = 0,
                X = previous is null ? Pos.Percent(55) : Pos.Right(previous) + 1,
            };
            launchButton.Accepting += (_, e) =>
            {
                e.Handled = true;
                LaunchBrowser(browser);
            };
            Add(launchButton);
            previous = launchButton;
        }

        var manualButton = new Button { Text = "Manual", Y = 1, X = Pos.Percent(55) };
        manualButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            StartManual();
        };

        var dnsButton = new Button { Text = "DNS", Y = 1, X = Pos.Right(manualButton) + 1 };
        dnsButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditDns();
        };

        _stopButton = new Button { Text = "Stop", Y = 1, X = Pos.Right(dnsButton) + 1, Enabled = false };
        _stopButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            _ = StopSessionAsync();
        };
        Add(manualButton, dnsButton, _stopButton);
    }

    private void LaunchBrowser(BrowserInfo browser)
    {
        try
        {
            var session = GetOrStartSession();
            int pid = BrowserLauncher.Launch(browser, session.Port, _nic.Id);
            session.AddLaunchedProcess(pid);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"error: {ex.Message}";
            return;
        }
        UpdateStatus();
    }

    private void StartManual()
    {
        try
        {
            GetOrStartSession();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"error: {ex.Message}";
            return;
        }
        UpdateStatus();
    }

    private ProxySession GetOrStartSession()
    {
        var session = _sessions.GetOrStart(_nic, _pendingDnsOverride);
        if (!ReferenceEquals(_hookedSession, session))
        {
            _hookedSession = session;
            session.Changed += () => Application.Invoke(UpdateStatus);
        }
        return session;
    }

    private void EditDns()
    {
        var result = DnsDialog.Show(_nic, _sessions.GetSession(_nic.Id)?.DnsOverride ?? _pendingDnsOverride);
        if (!result.Confirmed) return;
        _pendingDnsOverride = result.Server;
        _sessions.GetSession(_nic.Id)?.SetDnsOverride(result.Server);
        UpdateStatus();
    }

    private async Task StopSessionAsync()
    {
        try
        {
            await _sessions.StopAsync(_nic.Id);
        }
        catch
        {
            // Session teardown failures leave nothing actionable for the user.
        }
        Application.Invoke(UpdateStatus);
    }

    private void UpdateStatus()
    {
        var session = _sessions.GetSession(_nic.Id);
        _stopButton.Enabled = session is not null;
        if (session is null)
        {
            _statusLabel.Text = _pendingDnsOverride is null
                ? "stopped"
                : $"stopped · DNS override pending: {_pendingDnsOverride}";
        }
        else
        {
            var text = $"proxy {session.ProxyUrl} · {session.LaunchedProcessIds.Count} browser(s) · {session.ActiveConnections} conn(s)";
            if (session.DnsOverride is not null) text += $" · DNS {session.DnsOverride}";
            if (session.LastError is not null) text += $" · last error: {session.LastError}";
            _statusLabel.Text = text;
        }
        SetNeedsDraw();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string ShortName(string name)
    {
        var shortened = name.Replace("Google ", "").Replace("Microsoft ", "");
        return Truncate(shortened, 12);
    }
}
```

`src/BindProxy.Tui/DnsDialog.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using BindProxy.Core.Nics;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BindProxy.Tui;

public static class DnsDialog
{
    /// <summary>Asks for a DNS override. Blank input means "use the NIC's own DNS servers".</summary>
    public static (bool Confirmed, IPAddress? Server) Show(NicInfo nic, IPAddress? current)
    {
        (bool Confirmed, IPAddress? Server) result = (false, null);

        var dialog = new Dialog { Title = $"DNS override — {nic.Name}", Width = 52, Height = 9 };
        var hint = new Label { X = 1, Y = 0, Text = "DNS server IPv4 (blank = NIC default):" };
        var field = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = current?.ToString() ?? "" };
        var error = new Label { X = 1, Y = 2, Width = Dim.Fill(1), Text = "" };

        var ok = new Button { Text = "OK", X = 1, Y = 4, IsDefault = true };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            var text = field.Text.Trim();
            if (text.Length == 0)
            {
                result = (true, null);
                Application.RequestStop(dialog);
            }
            else if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            {
                result = (true, ip);
                Application.RequestStop(dialog);
            }
            else
            {
                error.Text = "Enter a valid IPv4 address or leave blank.";
            }
        };

        var cancel = new Button { Text = "Cancel", X = Pos.Right(ok) + 2, Y = 4 };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(dialog);
        };

        dialog.Add(hint, field, error, ok, cancel);
        Application.Run(dialog);
        dialog.Dispose();
        return result;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: success. If Terminal.Gui member names differ, apply only the mechanical fixes listed in the API note above.

- [ ] **Step 3: Run all tests**

Run: `dotnet test tests/BindProxy.Core.Tests`
Expected: all tests PASS (Core untouched; this catches accidental Core edits).

- [ ] **Step 4: Manual smoke test — PAUSE for a human**

Run: `dotnet run --project src/BindProxy.Tui`

Human checklist (report results, do not skip):
1. One bordered row per physical NIC, with name, IPv4, DNS servers.
2. Tab/arrows move focus between buttons; mouse clicks work.
3. `Manual` on a row → status shows `proxy http://127.0.0.1:<port> · 0 browser(s) · 0 conn(s)`; verify with `curl -x http://127.0.0.1:<port> https://api.ipify.org` from another terminal — it should return that NIC's public IP.
4. A browser button (e.g. Chrome) → browser opens; visiting https://api.ipify.org shows the selected NIC's public IP; conn count in the row moves.
5. `DNS` → dialog; entering `1.1.1.1` shows `DNS 1.1.1.1` in status; blank clears it; garbage shows the inline error.
6. `Stop` → status returns to `stopped`, browser loses connectivity.
7. Esc quits; `netstat -ano | findstr <port>` shows the listener is gone.

- [ ] **Step 5: Commit**

```bash
git add src/BindProxy.Tui
git commit -m "feat: Terminal.Gui NIC-row TUI"
```

---

### Task 11: AOT publish + README

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: the finished solution.
- Produces: a published single-directory AOT exe and user docs.

- [ ] **Step 1: Publish with Native AOT**

Run: `dotnet publish src/BindProxy.Tui -c Release -r win-x64`
Expected: success. **Zero IL2xxx/IL3xxx warnings attributed to `BindProxy.Core` or `BindProxy.Tui` sources** (warnings from Terminal.Gui internals are tolerated; record them in the commit message if present). Output: `src/BindProxy.Tui/bin/Release/<tfm>/win-x64/publish/BindProxy.Tui.exe`.

- [ ] **Step 2: Verify the published exe — PAUSE for a human**

Run the published `BindProxy.Tui.exe` directly (not `dotnet run`). Human confirms the TUI opens, one `Manual` session starts, and `curl -x` through it works — same as Task 10 checks 1 and 3.

- [ ] **Step 3: Write the README**

`README.md`:

```markdown
# BindProxy

Pin a browser to a specific network interface (NIC) on Windows.

BindProxy runs a tiny local HTTP proxy per NIC. Every outbound connection the
proxy makes — including its DNS queries — is bound to that NIC's IPv4 address,
so the traffic leaves through that interface regardless of the Windows routing
table. Chromium browsers are launched pre-configured to use the proxy.

## Usage

Run `BindProxy.Tui.exe`. Each NIC gets a row:

- **Browser buttons** (Chrome, Edge, …) — start that NIC's proxy (if needed)
  and launch the browser through it. Repeated launches on one NIC share one
  proxy. Browsers get an isolated profile per browser+NIC under
  `%LOCALAPPDATA%\BindProxy\Profiles`, so cookies/history stay separated.
- **Manual** — start the proxy without a browser; the row shows
  `http://127.0.0.1:<port>` for use in any app that supports an HTTP proxy.
- **DNS** — optional DNS override for that NIC's session (blank = the NIC's
  own DNS servers). DNS queries always go out through the selected NIC.
- **Stop** — stop that NIC's proxy. Esc quits and stops everything.

## Building

- .NET 10 SDK, Windows.
- `dotnet test` — run the test suite.
- `dotnet publish src/BindProxy.Tui -c Release -r win-x64` — Native AOT exe.

## Caveats (v1)

- IPv4 only; A records only.
- Chromium-family browsers only (Firefox needs generated profiles — later).
- WebRTC can bypass proxies by design; this tool does not attempt to stop it.
- Plain `http://` requests are forwarded one-request-per-connection
  (`Connection: close`); HTTPS (CONNECT) tunnels are unaffected. In practice
  almost all traffic is HTTPS.
- The proxy is loopback-only and unauthenticated: any local process can use it.
```

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: README with usage and v1 caveats"
```

---

## Deviations from the spec

- The spec names a `LaunchOptions` type; the plan realizes it as the `ProfileMode` enum parameter on `BrowserLauncher.Launch` — same internal knob, less ceremony. Update the spec if this matters.
- `ConnectionError`/`ConnectionSucceeded` events on `ProxyServer` implement the spec's "row shows an error state" requirement (surfaced as `ProxySession.LastError`, cleared by the next successful connection).





