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
