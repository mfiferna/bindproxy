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
    public long TotalBytesSent => _server.TotalBytesSent;
    public long TotalBytesReceived => _server.TotalBytesReceived;
    public double SentBytesPerSecond => _server.SentBytesPerSecond;
    public double ReceivedBytesPerSecond => _server.ReceivedBytesPerSecond;

    public IReadOnlyList<int> LaunchedProcessIds
    {
        get { lock (_lock) return _pids.ToArray(); }
    }

    /// <summary>Raised on any observable change: connections, DNS, PIDs, errors.</summary>
    public event Action? Changed;

    internal ProxySession(NicInfo nic, IPAddress? dnsOverride, ConnectionErrorLog errorLog)
    {
        Nic = nic;
        DnsOverride = dnsOverride;
        _resolver = new SwappableResolver(BuildResolver(nic, dnsOverride));
        _server = new ProxyServer(nic.Ipv4Address, _resolver);
        _server.ActiveConnectionsChanged += () => Changed?.Invoke();
        _server.ThroughputChanged += () => Changed?.Invoke();
        _server.ConnectionError += message =>
        {
            LastError = message;
            errorLog.Add(Nic.Name, message);
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
