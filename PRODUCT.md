# Product

## Register

product

## Users

Two overlapping groups on Windows, both with more than one network connection (typically Ethernet + a metered mobile WiFi hotspot):

- **Power users** who understand NICs/proxies/DNS and want precise, direct control over which interface a browser session uses.
- **Less experienced users** who just want to keep a specific browser (or profile) from draining their metered mobile data — they need the app to be understandable at a glance without knowing what a "NIC" or "proxy" is.

Both groups pick a connection and launch a browser bound to it in as few interactions as possible. The tool runs locally, unattended, often left open in the background while the user works.

## Product Purpose

BindProxy binds browser traffic to a specific network interface so a user can, for example, keep general browsing on cheap wired Ethernet while explicitly choosing when something uses the metered WiFi hotspot (or vice versa). It runs a local per-NIC proxy + DNS resolution pinned to that NIC and launches Chromium-family browsers pointed at it. Success = a user glances at the window, understands which connections are available, and gets a browser open on the right one in two clicks — without needing to understand proxies, ports, or DNS.

## Brand Personality

Precise, trustworthy, unobtrusive. Voice is plain and concrete, never jargon-forward — a "connection" not an "interface adapter," a "browser window on this connection" not "launch with proxy binding." Confidence comes from clarity and restraint, not decoration. It should feel like a small, well-made native Windows utility that respects the user's attention: quiet by default, clear when it matters (errors, running state), never cutesy or salesy.

## Anti-references

- Generic SaaS admin dashboard (hero metric cards, colorful stat tiles, sidebar nav for a single-screen tool).
- Cutesy/playful consumer app styling — this is a technical utility, not a lifestyle app.
- Corporate enterprise admin-panel chrome (dense toolbars, endless identical gray buttons per row — this is close to the current problem: every NIC card doubles its button count with near-identical "Browser" / "Browser (default profile)" pairs, and Czech copy that reads as literally translated English).
- Anything that requires the user to learn proxy/DNS terminology before they can complete the core task of "open my browser on this connection."

## Design Principles

1. **Understand at a glance.** A user who has never seen the app should be able to tell, within a second, which connections exist, which is active, and how to launch a browser — no legend needed.
2. **One clear action per connection, not a wall of buttons.** Group browser choice and profile mode (isolated vs. default) as one decision, not two parallel rows of buttons.
3. **Plain language over technical precision.** Prefer "connection" over "NIC," "keep browsing separate" over "isolated profile," etc. Technical detail (proxy URL, port, DNS) stays available but secondary/inspectable, not front-and-center.
4. **Status is legible without reading.** Running/stopped/error states should be visually distinct (not just color text) at a glance across a list of connections.
5. **Parity with the TUI.** Copy and grouping decisions should stay consistent between the Avalonia UI and the Terminal.Gui frontend where reasonably possible.

## Accessibility & Inclusion

- Standard WCAG AA contrast (≥4.5:1 body text, ≥3:1 large text/UI components).
- Fully keyboard-operable (Tab/Enter/Arrows), mirroring the TUI's keyboard-first interaction.
- Not screen-reader-optimized as a hard requirement, but must not be actively hostile to one (proper control types/labels, no meaning conveyed by color alone).
- Two supported languages (English/Czech) with OS auto-detection and a manual switch; layout must not break when Czech strings run longer than English ones.
