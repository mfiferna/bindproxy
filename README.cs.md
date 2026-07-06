[🇬🇧 English](README.md) | 🇨🇿 **Čeština**

> **⚠️ Upozornění ohledně AI:** Tento projekt byl vytvořen téměř výhradně AI asistenty pro programování (Claude Code a GitHub Copilot CLI), s lidskou kontrolou a vedením. Podle toho k němu přistupujte — než mu svěříte cokoliv citlivého, přečtěte si kód.

# BindProxy

Vyberte síťové připojení, spusťte přes něj prohlížeč. Nic dalšího ve vašem PC se nezmění.

![Snímek obrazovky BindProxy](docs/assets/screenshot.png)

BindProxy spustí lokální forward proxy připnutou k vybrané síťové kartě (se shodným DNS) a poté spustí váš prohlížeč nastavený tak, aby ji používal — takže jedno okno/profil prohlížeče může jít ven přes Wi-Fi, zatímco všechno ostatní dál používá Ethernet, nebo naopak.

## Stáhnout

**[⬇ Stáhnout pro Windows](https://github.com/mfiferna/bindproxy/releases/latest/download/bindproxy-win-x64.zip)** — rozbalte a spusťte `.exe`, není potřeba nic instalovat.

Preferujete terminálové rozhraní, nebo chcete starší verzi? Podívejte se na [stránku Releases](https://github.com/mfiferna/bindproxy/releases).

## Sestavení ze zdrojového kódu

```
git clone https://github.com/mfiferna/bindproxy.git
cd bindproxy
run.bat        # desktopové rozhraní Avalonia
run-tui.bat    # terminálové rozhraní
```

Vyžaduje [.NET 10 SDK](https://dotnet.microsoft.com/download). Vlastní kompaktní samostatně spustitelné release verze si můžete sestavit pomocí [`release.bat`](release.bat) / [`release.sh`](release.sh).
