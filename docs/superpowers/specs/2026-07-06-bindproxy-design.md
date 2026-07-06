# BindProxy — Design

**Date:** 2026-07-06
**Status:** Approved design, pre-implementation

## Purpose

A small local Windows app that pins a browser's traffic to a specific network interface (NIC). It runs a local HTTP forward proxy per NIC whose outbound sockets are bound to that NIC's IP, and launches browsers pointed at that proxy. The NIC/proxy mechanics are opaque to the user; the only surfaced knob is an optional DNS server override.

## Goals

- List usable NICs; launch any installed Chromium-family browser bound to a chosen NIC in ~two interactions.
- **Manual start**: start a per-NIC proxy without launching a browser and display its connection details (`http://127.0.0.1:PORT`) for use in any app.
- Simple, readable code. Native AOT publishing for the whole app.
- UI-agnostic core: TUI first (Terminal.Gui v2), Avalonia later, both consuming the same core library.

## Non-goals (v1)

- Firefox/Gecko support (needs generated profiles; add later behind `BrowserCatalog`/`BrowserLauncher`).
- IPv6 (v1 resolves A records and binds IPv4 only; AAAA + v6 bind is a clean later addition).
- WebRTC leak-proofing (browser-side behavior; noted in README, out of scope).
- Config persistence (nothing to persist yet; when added, use source-generated `System.Text.Json` for AOT).
- TLS interception of any kind — the proxy only tunnels bytes.

## Architecture

```
BindProxy.Core   class library, zero UI dependencies
├── Nics/        NicInfo, NicCatalog
├── Browsers/    BrowserInfo, BrowserCatalog
├── Dns/         DnsResolver
├── Proxy/       ProxyServer, ProxyConnection
├── Sessions/    ProxySession, SessionManager
└── Launch/      BrowserLauncher, LaunchOptions

BindProxy.Tui    Terminal.Gui v2 (2.4.x) executable, PublishAot=true
BindProxy.Core.Tests  xUnit
(BindProxy.Avalonia — future, consumes the same Core surface)
```

The Core exposes plain async APIs and events (session started/stopped, connection count changed, session error). The UI contract is effectively "a list of NIC rows plus actions" — the same shape both TUI and a future Avalonia UI bind to.

### Core binding principle

Every outbound socket the proxy opens — TCP to origin servers and UDP for DNS — is explicitly `Bind()`-ed to the selected NIC's local IPv4 address before connecting. Windows then routes those packets out that interface. This is the entire mechanism; there is no routing-table or WFP manipulation.

## Components

### NicCatalog / NicInfo

Enumerates `NetworkInterface.GetAllNetworkInterfaces()`, keeping interfaces that are `Up`, have an IPv4 unicast address, and are not loopback. `NicInfo` carries: id, friendly name, description, IPv4 address, and the NIC's configured DNS servers (`GetIPProperties().DnsAddresses`, IPv4 only).

### Sessions — per-NIC

`SessionManager.GetOrStartAsync(nic, dnsOverride)` returns the existing running session for that NIC or starts a new one. **Sessions are keyed per-NIC, not per-launch**: launching three browsers on the same NIC reuses one proxy. A `ProxySession` owns:

- its `ProxyServer` (listening on `127.0.0.1:<OS-assigned ephemeral port>` — port conflicts impossible by construction),
- the effective DNS setting (NIC default or override),
- a list of launched browser PIDs (display only; browsers are not babysat — sessions stop when the user stops them),
- one `CancellationTokenSource` whose cancellation tears down the listener and all active tunnels.

`SessionManager.Dispose()` stops everything (hooked to Quit and Ctrl+C).

### ProxyServer / ProxyConnection

A `TcpListener` accepting connections; each becomes an independent `ProxyConnection` task:

- **`CONNECT host:port`** (all HTTPS): resolve host via `DnsResolver`, open a socket bound to the NIC IP, reply `200 Connection Established`, then pump bytes bidirectionally until either side closes. No TLS involvement.
- **Plain HTTP** (absolute-URI request line): resolve, connect bound to NIC IP, rewrite request line to origin-form (`GET http://h/p` → `GET /p`), strip `Proxy-Connection` header, forward, then tunnel bytes until close. No response parsing.

Protocol handling lives in one isolated class so a SOCKS5 listener could slot in later, but v1 is HTTP-only (universal `--proxy-server` support, `curl -x`-debuggable).

### DnsResolver

Minimal hand-rolled DNS client (~150 lines): builds an A-record query, sends it over a UDP socket **bound to the NIC's IP** to the NIC's configured DNS servers (tried in order, with timeout/retry), parses A records (handling name compression). If the session has a DNS override, that server is queried instead — still over the NIC. In-memory cache honoring TTL, capped at 5 minutes. Changing the override while a session is running applies to new lookups and flushes the cache; existing tunnels are unaffected.

Hand-rolled rather than DnsClient.NET because AOT behavior is then fully under our control and only one query type is needed.

### BrowserCatalog / BrowserLauncher

**Detection:** read `SOFTWARE\Clients\StartMenuInternet` under HKLM, HKCU, and WOW6432Node; take each browser's exe from `shell\open\command`; keep only known Chromium-family exe names (`chrome.exe`, `msedge.exe`, `brave.exe`, `vivaldi.exe`, `opera.exe`, `chromium.exe`). Dedupe by exe path.

**Launch:**

```
<exe> --proxy-server=http://127.0.0.1:<port>
      --proxy-bypass-list=<-loopback>
      --user-data-dir=%LOCALAPPDATA%\BindProxy\Profiles\<browser>-<nicId>
      --no-first-run --no-default-browser-check
```

`LaunchOptions.ProfileMode` (internal, not exposed in the TUI yet):

- `Isolated` (default): the `--user-data-dir` above. Required for `--proxy-server` to apply reliably, and doubles as per-NIC cookie/history separation. Profiles persist across runs.
- `UserDefault`: omit `--user-data-dir`. **Caveat:** only effective when no instance of that browser is already running — Chromium silently ignores `--proxy-server` when it merely opens a new window in an existing process. A future UI may detect a running process and warn.

## TUI (Terminal.Gui v2)

Main window: a vertical, scrollable stack of **NIC row views**. Each row is a bordered view (~4 rows tall, full width):

- **Left:** NIC friendly name, IPv4, DNS servers, live status line — `stopped` / `proxy http://127.0.0.1:PORT · 2 browsers · 14 conns` / error text.
- **Right:** buttons — one per detected browser (`[Chrome] [Edge] …`), `[Manual]`, `[DNS]`, `[Stop]` (enabled only while running).

Interaction: Tab/arrows + Enter, plus native Terminal.Gui mouse support. StatusBar at the bottom with key hints and Quit. `[DNS]` opens a dialog with a TextField (blank = NIC default). `[Manual]` starts the session; the proxy URL appears in the row status. Core session events update rows via `Application.Invoke`.

If many browsers are installed the button strip may crowd; acceptable for v1 (buttons are compact), revisit with a picker if it becomes real.

## Error handling

The proxy must never crash the app:

- Per-connection failures (DNS failure, unreachable host, connection reset) close only that connection. Before a tunnel is established the client gets `502 Bad Gateway` (resolution/connect failure) or `504` (timeout) with a one-line reason; after establishment, just close.
- NIC disappears mid-session: outbound binds start failing; the session stays up and its row shows an error state; the user stops it manually.
- No usable NICs or no browsers detected: friendly empty-state message.
- Browser exe fails to start: surfaced in the row status; session unaffected.

## Testing (xUnit, targets Core)

- **DnsResolver:** packet build/parse round-trips against captured real response bytes; fake UDP server for timeout/retry and server-order fallback.
- **ProxyServer:** loopback end-to-end — fake origin `TcpListener`, proxy bound to `127.0.0.1` as the "NIC", real `HttpClient` configured with the proxy; assert CONNECT tunneling and plain-HTTP rewriting.
- **BrowserCatalog:** parsing against injected fake registry data (small interface over registry reads).
- TUI: compile + manual smoke only.

## AOT constraints

- All projects: `PublishAot=true` (Tui is the published artifact), no reflection-based serialization, no `dynamic`.
- Dependencies: Terminal.Gui 2.4.x (AOT-viable, verified by maintainer's AOT fixes/tests), Spectre.Console **not** used.
- Trim warnings treated as errors so regressions surface at build time.

## Decisions log

| Decision | Choice | Why |
|---|---|---|
| Proxy protocol | HTTP CONNECT (+ plain HTTP) | Universal browser support, debuggable; SOCKS5 slot-in possible later |
| DNS | NIC's own servers by default, optional per-session override, queried over the NIC | Opaque and leak-free by default |
| Sessions | Per-NIC, shared by all launches on that NIC | Matches mental model; simple state |
| Browsers | Chromium-family only | `--proxy-server` flag; Firefox later |
| TUI library | Terminal.Gui v2 (2.4.16+) | NIC-row layout with real buttons/focus/mouse; AOT-viable per repo fixes/tests |
| UI shape | NIC rows with inline action buttons | Direct manipulation; maps 1:1 to future Avalonia view models |
| Profile handling | Internal `ProfileMode` option, `Isolated` hardcoded for now | Reliability of `--proxy-server`; `UserDefault` kept for later exposure |
