# BindProxy — Per-NIC Throughput Readout — Design

**Date:** 2026-07-06
**Status:** Approved design, pre-implementation

## Purpose

Show a live, per-NIC throughput readout in the Avalonia UI: a rolling-average current rate in each direction, plus cumulative bytes transferred since the session started. Runtime-only — nothing is persisted across app restarts or proxy stop/start.

## Non-goals

- TUI support (Avalonia only for now).
- Persisting throughput history across restarts or session stop/start (a new `ProxyServer` instance starts at zero).
- Per-connection throughput (only aggregated per-NIC/per-session).
- A graphical sparkline/history graph — plain auto-updating numbers only.
- Instrumenting the small amount of protocol overhead outside the pump loop (the `200 Connection Established` response, the rewritten HTTP request head, and any body bytes already captured in the initial 32KB header-peek buffer). Negligible for a throughput monitor; not worth complicating those code paths.

## Architecture

```
BindProxy.Core/Proxy/
├── ThroughputMeter.cs   new — cumulative totals + rolling-average rate, injectable clock
├── ProxyServer.cs       owns a ThroughputMeter, a 1s timer, raises ThroughputChanged
└── ProxyConnection.cs   PumpAsync copies via a manual loop, reports bytes per chunk

BindProxy.Core/Sessions/
└── ProxySession.cs      pass-through properties + bubbles ThroughputChanged into Changed

BindProxy.Avalonia/
└── MainWindow.axaml.cs  new formatter + a line in BuildSessionDetailRow
```

## Components

### ThroughputMeter

New sealed class in `BindProxy.Core.Proxy`. Tracks, for one `ProxyServer`'s lifetime:

- `TotalBytesSent` / `TotalBytesReceived` (`long`, cumulative, thread-safe increments via `Interlocked`). "Sent" = client → origin (upload). "Received" = origin → client (download).
- `SentBytesPerSecond` / `ReceivedBytesPerSecond` (`double`) — a 5-second rolling average, recomputed on each `Tick()`.

```csharp
public sealed class ThroughputMeter(TimeSpan window, Func<DateTime>? clock = null)
{
    public void AddSent(int byteCount);
    public void AddReceived(int byteCount);
    public void Tick(); // samples current totals, trims samples older than `window`, recomputes rate
}
```

Internally keeps a small queue of `(Time, Sent, Received)` samples, one appended per `Tick()`, trimmed to the configured window. Rate = `(currentTotal - oldestSampleInWindow) / elapsedSeconds`. Before 5 seconds of history exist, it computes the rate over whatever history is available rather than waiting — avoids a misleading "0" for the first few seconds. The clock is injectable so tests can drive it without real timers or sleeps.

### ProxyServer changes

- Owns one `ThroughputMeter` with a 5-second window.
- A `System.Threading.Timer`, started in `Start()` and disposed in `DisposeAsync()`, ticks the meter every 1 second and raises a new `event Action? ThroughputChanged`.
- Per-connection byte callbacks (see below) call `meter.AddSent`/`AddReceived` directly — these are incremental, firing as data actually flows, not just at connection close. This is what makes a long-lived transfer (a large download, a long-open tunnel) show up in the live rate while it's happening.
- Exposes `TotalBytesSent`, `TotalBytesReceived`, `SentBytesPerSecond`, `ReceivedBytesPerSecond` as pass-through properties over the meter.

### ProxyConnection changes

`PumpAsync`/`CopySilentlyAsync` currently use `Stream.CopyToAsync`, which gives no visibility into bytes copied. Replace with a manual buffered loop (same 81920-byte default buffer size CopyToAsync uses internally, via `ArrayPool<byte>.Shared`, so no perf regression) that invokes an `Action<int>` with the chunk size after each write, for each direction independently. `RunAsync` gains two new callback parameters (`Action<int> onBytesSent`, `Action<int> onBytesReceived`), following the same pattern as the existing `onError`/`onSuccess` callbacks.

### ProxySession changes

Pass-through properties mirroring `ActiveConnections`, plus:

```csharp
_server.ThroughputChanged += () => Changed?.Invoke();
```

No new event type needed — this reuses the existing `Changed` event and its existing subscription wiring in `MainWindow.axaml.cs` (`SyncSessionSubscriptions`), so the Avalonia UI gets a free 1-second refresh while a session is running with no new UI-side timer.

### Avalonia display

In `BuildSessionDetailRow`, shown only while a session is running, a new line under the existing proxy-address/connections line:

```
↓ 12.4 Mbps · ↑ 1.2 Mbps
↓ 428.6 MB · ↑ 51.2 MB total
```

New static formatter (small, presentation-only, lives in the Avalonia project — not shared with the TUI since this feature is Avalonia-only):

- **Rate**: `bytesPerSecond * 8` → bits/sec → auto-scale decimal (1000-based, matching standard networking convention for bps units): floor at Mbps (never drops to Kbps — a value under 1 Mbps still renders as e.g. `0.1 Mbps`), ceiling at Gbps (crosses over at 1000 Mbps). One decimal place always.
- **Amount**: bytes → auto-scale decimal (1000-based, for consistency with the rate's units rather than mixing in binary MiB/GiB): floor at MB, ceiling at GB. One decimal place.

New `TextKey` entries (English + Czech) for the two line templates, following the existing `Localizer` pattern (e.g. `ThroughputRateLine = "↓ {0} · ↑ {1}"`, `ThroughputTotalLine = "↓ {0} · ↑ {1} total"`).

## Error handling

No new failure modes: byte-counting callbacks are simple counter increments with no I/O or exceptions of their own. If a connection's pump loop throws/cancels (existing behavior, unchanged), whatever bytes were already reported via the callback remain counted — consistent with "best-effort live readout," not an exactly-once ledger.

## Testing

- **`ThroughputMeter`** (new test file): fake-clock-driven unit tests — rate after N seconds of known byte additions, window trimming behavior, zero-traffic behavior (rate stays 0, no divide-by-zero), behavior before a full window of history exists.
- **`ProxyServer`/`ProxyConnection`**: extend the existing loopback CONNECT-tunnel test setup to push a known payload size in each direction and assert `TotalBytesSent`/`TotalBytesReceived` match exactly.
- **Avalonia formatter**: pure-function unit tests for boundary cases — just under/at/over 1 Mbps, just under/at/over 1000 Mbps (Gbps crossover), just under/at/over 1 MB, just under/at/over 1000 MB (GB crossover).

## Decisions log

| Decision | Choice | Why |
|---|---|---|
| UI scope | Avalonia only | User's explicit choice; TUI can follow later behind the same Core surface |
| Rate unit | Mbps floor, Gbps ceiling, bits not bytes | User's explicit choice; standard networking convention |
| Amount unit | MB/GB, decimal-scaled | User's explicit choice; kept consistent with the rate's decimal scaling |
| Averaging | 5-second rolling window, ticked every 1s | User's explicit choice; smooths single-tick bursts while staying responsive |
| Byte counting | Incremental, inside the pump loop | Only way a long-lived transfer shows up in a *live* rate before it closes |
| Update mechanism | Reuse existing `Changed` event via a Core-side timer | No new UI-side timer; keeps Core UI-agnostic and reuses existing subscription wiring |
