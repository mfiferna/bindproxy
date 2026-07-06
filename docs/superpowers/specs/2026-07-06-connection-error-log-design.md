# BindProxy — Connection Error Log — Design

**Date:** 2026-07-06
**Status:** Approved design, pre-implementation

## Purpose

Connection errors (DNS failure, connect timeout, bad request, etc.) already surface as a transient red status line per NIC, but it's cleared as soon as the next connection on that NIC succeeds — on a busy site like YouTube opening many parallel connections, the message can disappear before it's readable. Add a reviewable, in-app log of these errors across all sessions.

## Non-goals

- TUI support (Avalonia only, matching the throughput feature's scope).
- Persistence across app restarts — runtime-only, in-memory.
- Changing the existing transient per-NIC status line/`LastError` behavior — this feature adds a permanent record alongside it, not a replacement.

## Architecture

```
BindProxy.Core/Sessions/
├── ConnectionErrorLog.cs   new — capped ring buffer, thread-safe, raises EntryAdded
├── SessionManager.cs       owns one ConnectionErrorLog, passes it to each ProxySession
└── ProxySession.cs         appends to the log on ConnectionError, alongside existing LastError handling

BindProxy.Avalonia/
└── MainWindow.axaml(.cs)   TabControl: "Connections" (existing rows) / "Log" (new)
```

## Components

### ConnectionErrorLog

New in `BindProxy.Core.Sessions`:

```csharp
public sealed record ConnectionErrorLogEntry(DateTime TimestampUtc, string NicName, string Message);

public sealed class ConnectionErrorLog(int capacity = 200)
{
    public event Action? EntryAdded;
    public IReadOnlyList<ConnectionErrorLogEntry> Entries { get; } // snapshot, newest last
    public void Add(string nicName, string message);
    public void Clear();
}
```

Thread-safe: `Add` can be called concurrently from any session's connection-handling threads. Capacity-capped at 200 (oldest evicted first) — generous enough for a long browsing session without unbounded growth.

### SessionManager

Owns one `ConnectionErrorLog` instance for the manager's lifetime, exposed as `public ConnectionErrorLog ErrorLog { get; }`. Passes it into every `ProxySession` it constructs in `GetOrStart`.

### ProxySession

Constructor takes the shared `ConnectionErrorLog`. The existing handler:

```csharp
_server.ConnectionError += message =>
{
    LastError = message;
    Changed?.Invoke();
};
```

gains one line — `errorLog.Add(Nic.Name, message);` — alongside the existing behavior, unchanged otherwise.

### Avalonia UI

The header (title, summary, language/refresh controls) stays outside the tabs. Below it, a `TabControl` with two tabs:

- **Connections** — today's NIC-rows view, unchanged.
- **Log** — a scrollable list, newest entry first, each row formatted like `21:14:03 · Ethernet 2 · connect timeout: youtube.com:443`. An empty-state message when there are no entries. A **Clear** button that calls `ErrorLog.Clear()`.

The Log tab refreshes by subscribing to `sessionManager.ErrorLog.EntryAdded`, marshaled to the UI thread the same way `SessionsChanged`/`session.Changed` already are (`Dispatcher.UIThread.Post`).

New `TextKey` entries (English + Czech): tab labels ("Connections" / "Log"), the empty-state message, and the Clear button label.

## Error handling

No new failure modes — `ConnectionErrorLog.Add` is a simple in-memory append with no I/O. If a session's `ConnectionError` fires during app shutdown/teardown, the log entry is just retained in memory (harmless; the whole process is likely exiting anyway).

## Testing

- **`ConnectionErrorLog`**: unit tests for capacity capping/eviction order (oldest dropped first once over 200), `EntryAdded` firing on `Add`, `Clear()` emptying `Entries`.
- **`SessionManager`/`ProxySession`**: extend the existing `Failed_connection_sets_LastError` test pattern in `SessionManagerTests.cs` — trigger a real connection failure through a session and assert `manager.ErrorLog.Entries` contains an entry with the expected NIC name and message text.
- **Avalonia tab wiring**: manual verification only (no test infrastructure for the Avalonia project), consistent with the throughput feature.

## Decisions log

| Decision | Choice | Why |
|---|---|---|
| UI scope | Avalonia only | Matches throughput feature precedent; TUI can follow later behind the same Core log |
| Log scope | One combined list across all NICs, tagged by NIC name | User's explicit choice; simplest place to check, no hunting per-NIC |
| Log owner | `SessionManager` | Only object that already knows about every session; `ProxyServer` shouldn't know about sibling NICs |
| Feed mechanism | Subscribe directly to `ProxyServer.ConnectionError` | `LastError`/`Changed` can race with a subsequent success clearing the message before it's read |
| Capacity | 200 entries, oldest evicted | User's explicit choice; bounds memory on long sessions |
| Clear button | Yes | User's explicit choice; standard for a log view |
| Persistence | None — runtime-only | Matches the project's existing "nothing to persist yet" stance (see original design spec) |
