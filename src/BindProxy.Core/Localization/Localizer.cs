using System.Globalization;

namespace BindProxy.Core.Localization;

public static class Localizer
{
    private static readonly CultureInfo CzechCulture = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

    private static readonly IReadOnlyDictionary<TextKey, string> Czech = new Dictionary<TextKey, string>
    {
        [TextKey.AppTitle] = "BindProxy",
        [TextKey.Language] = "Jazyk",
        [TextKey.EnglishLanguage] = "English",
        [TextKey.CzechLanguage] = "Čeština",
        [TextKey.Quit] = "Konec",
        [TextKey.ExitBindProxy] = "Ukončit BindProxy",
        [TextKey.Refresh] = "Obnovit",
        [TextKey.RefreshNicAndBrowserLists] = "Znovu najít připojení a prohlížeče",
        [TextKey.NoUsableNicsDetected] = "Nenašli jsme žádné použitelné připojení k internetu.",
        [TextKey.NoChromiumDetectedManualAvailable] = "Nenašli jsme žádný prohlížeč, ale proxy si můžete spustit ručně.",
        [TextKey.MainInstruction] = "Vyberte připojení a otevřete na něm prohlížeč.",
        [TextKey.NicRowInstruction] = "Ve výchozím prohlížeči nejdřív zavřete všechna jeho okna.",
        [TextKey.NoChromiumFound] = "Nenašli jsme žádný prohlížeč",
        [TextKey.Manual] = "Ručně",
        [TextKey.Dns] = "DNS",
        [TextKey.Stop] = "Zastavit",
        [TextKey.DefaultProfileButton] = "{0} výchozí profil",
        [TextKey.DnsDialogTitle] = "Vlastní DNS – {0}",
        [TextKey.DnsDialogPrompt] = "Nechte prázdné pro výchozí DNS server tohoto připojení.",
        [TextKey.Cancel] = "Zrušit",
        [TextKey.Apply] = "Použít",
        [TextKey.InvalidDnsTitle] = "Neplatná DNS adresa",
        [TextKey.InvalidDnsMessage] = "„{0}“ není platná IP adresa.",
        [TextKey.Ok] = "OK",
        [TextKey.LaunchFailedTitle] = "Prohlížeč se nepodařilo spustit",
        [TextKey.StartProxyFailedTitle] = "Proxy se nepodařilo spustit",
        [TextKey.StopProxyFailedTitle] = "Proxy se nepodařilo zastavit",
        [TextKey.DnsLabel] = "DNS: {0}",
        [TextKey.Ipv4Label] = "IPv4: {0}",
        [TextKey.DnsOverrideSuffix] = "{0} (vlastní)",
        [TextKey.SystemDefaultUnavailable] = "systémové DNS není k dispozici",
        [TextKey.None] = "žádné",
        [TextKey.Stopped] = "zastaveno",
        [TextKey.PendingDns] = "zastaveno · čeká vlastní DNS {0}",
        [TextKey.ProxyStatus] = "proxy {0}",
        [TextKey.BrowsersCount] = "{0} prohlížečů",
        [TextKey.ConnectionsCount] = "{0} spojení",
        [TextKey.ErrorStatus] = "chyba: {0}",
        [TextKey.BrowserAlreadyRunning] = "{0} už běží. Nejdřív zavřete všechna jeho okna – jinak si nemusí nastavení proxy všimnout.",
        [TextKey.FailedToStartBrowser] = "{0} se nepodařilo spustit",

        [TextKey.AppSubtitle] = "Vyberte připojení a spusťte přes něj prohlížeč.",
        [TextKey.NicKindEthernet] = "Kabelové připojení",
        [TextKey.NicKindWireless] = "Wi-Fi",
        [TextKey.NicKindOtherConnection] = "Jiné připojení",
        [TextKey.StatusRunningPill] = "Aktivní",
        [TextKey.StatusStoppedPill] = "Nespuštěno",
        [TextKey.StatusErrorPill] = "Vyžaduje pozornost",
        [TextKey.OpenWithBrowser] = "Otevřít {0}",
        [TextKey.UseExistingProfileCheckbox] = "Použít můj běžný prohlížeč (bez odděleného profilu)",
        [TextKey.UseExistingProfileTooltip] = "Vyžaduje, aby byl prohlížeč před spuštěním úplně zavřený, jinak Chromium nastavení proxy ignoruje.",
        [TextKey.NoBrowsersDetected] = "Nenašli jsme žádný prohlížeč. Proxy si můžete spustit ručně a použít ji v jiné aplikaci.",
        [TextKey.StartProxyOnly] = "Jen spustit proxy",
        [TextKey.ChangeDns] = "Změnit DNS",
        [TextKey.StopConnection] = "Zastavit",
        [TextKey.ConnectionAddressLine] = "{0} · DNS {1}",
        [TextKey.ProxyAddressLine] = "Proxy adresa: {0}",
        [TextKey.ActiveBrowsersAndConnections] = "{0} prohlížečů · {1} spojení",
        [TextKey.LastErrorLine] = "Chyba: {0}",
        [TextKey.DnsCustomSuffix] = "{0} (vlastní)",
        [TextKey.DnsSystemDefault] = "výchozí",
        [TextKey.DnsUnavailable] = "není k dispozici",
        [TextKey.ThroughputRateLine] = "↓ {0} · ↑ {1}",
        [TextKey.ThroughputTotalLine] = "↓ {0} · ↑ {1} celkem",
        [TextKey.ConnectionsTab] = "Připojení",
        [TextKey.LogTab] = "Log",
        [TextKey.LogEmptyState] = "Zatím žádné chyby připojení.",
        [TextKey.ClearLog] = "Vymazat",
        [TextKey.LogEntryLine] = "{0:HH:mm:ss} · {1} · {2}",
    };

    private static readonly IReadOnlyDictionary<TextKey, string> English = new Dictionary<TextKey, string>
    {
        [TextKey.AppTitle] = "BindProxy",
        [TextKey.Language] = "Language",
        [TextKey.EnglishLanguage] = "English",
        [TextKey.CzechLanguage] = "Čeština",
        [TextKey.Quit] = "Quit",
        [TextKey.ExitBindProxy] = "Exit BindProxy",
        [TextKey.Refresh] = "Refresh",
        [TextKey.RefreshNicAndBrowserLists] = "Look for connections and browsers again",
        [TextKey.NoUsableNicsDetected] = "No usable internet connection was found.",
        [TextKey.NoChromiumDetectedManualAvailable] = "No browser was found, but you can still start the proxy manually.",
        [TextKey.MainInstruction] = "Pick a connection, then open a browser on it.",
        [TextKey.NicRowInstruction] = "Close all windows of that browser first, or it won't pick up the proxy.",
        [TextKey.NoChromiumFound] = "No browser found",
        [TextKey.Manual] = "Manual",
        [TextKey.Dns] = "DNS",
        [TextKey.Stop] = "Stop",
        [TextKey.DefaultProfileButton] = "{0} Default",
        [TextKey.DnsDialogTitle] = "Custom DNS – {0}",
        [TextKey.DnsDialogPrompt] = "Leave blank to use this connection's default DNS server.",
        [TextKey.Cancel] = "Cancel",
        [TextKey.Apply] = "Apply",
        [TextKey.InvalidDnsTitle] = "Invalid DNS address",
        [TextKey.InvalidDnsMessage] = "\u201c{0}\u201d isn't a valid IP address.",
        [TextKey.Ok] = "OK",
        [TextKey.LaunchFailedTitle] = "Couldn't start the browser",
        [TextKey.StartProxyFailedTitle] = "Couldn't start the proxy",
        [TextKey.StopProxyFailedTitle] = "Couldn't stop the proxy",
        [TextKey.DnsLabel] = "DNS: {0}",
        [TextKey.Ipv4Label] = "IPv4: {0}",
        [TextKey.DnsOverrideSuffix] = "{0} (custom)",
        [TextKey.SystemDefaultUnavailable] = "no system DNS available",
        [TextKey.None] = "none",
        [TextKey.Stopped] = "stopped",
        [TextKey.PendingDns] = "stopped · custom DNS {0} pending",
        [TextKey.ProxyStatus] = "proxy {0}",
        [TextKey.BrowsersCount] = "{0} browsers",
        [TextKey.ConnectionsCount] = "{0} conns",
        [TextKey.ErrorStatus] = "error: {0}",
        [TextKey.BrowserAlreadyRunning] = "{0} is already running. Close all its windows first, or it won't pick up the proxy settings.",
        [TextKey.FailedToStartBrowser] = "Couldn't start {0}",

        [TextKey.AppSubtitle] = "Pick a connection and launch a browser through it.",
        [TextKey.NicKindEthernet] = "Wired connection",
        [TextKey.NicKindWireless] = "Wi-Fi",
        [TextKey.NicKindOtherConnection] = "Other connection",
        [TextKey.StatusRunningPill] = "Running",
        [TextKey.StatusStoppedPill] = "Not started",
        [TextKey.StatusErrorPill] = "Needs attention",
        [TextKey.OpenWithBrowser] = "Open {0}",
        [TextKey.UseExistingProfileCheckbox] = "Use my regular browser (no separate profile)",
        [TextKey.UseExistingProfileTooltip] = "The browser must be fully closed first, or Chromium will ignore the proxy setting.",
        [TextKey.NoBrowsersDetected] = "No browser was found. You can still start the proxy manually and point another app at it.",
        [TextKey.StartProxyOnly] = "Start proxy only",
        [TextKey.ChangeDns] = "Change DNS",
        [TextKey.StopConnection] = "Stop",
        [TextKey.ConnectionAddressLine] = "{0} · DNS {1}",
        [TextKey.ProxyAddressLine] = "Proxy address: {0}",
        [TextKey.ActiveBrowsersAndConnections] = "{0} browsers · {1} connections",
        [TextKey.LastErrorLine] = "Error: {0}",
        [TextKey.DnsCustomSuffix] = "{0} (custom)",
        [TextKey.DnsSystemDefault] = "default",
        [TextKey.DnsUnavailable] = "unavailable",
        [TextKey.ThroughputRateLine] = "↓ {0} · ↑ {1}",
        [TextKey.ThroughputTotalLine] = "↓ {0} · ↑ {1} total",
        [TextKey.ConnectionsTab] = "Connections",
        [TextKey.LogTab] = "Log",
        [TextKey.LogEmptyState] = "No connection errors yet.",
        [TextKey.ClearLog] = "Clear",
        [TextKey.LogEntryLine] = "{0:HH:mm:ss} · {1} · {2}",
    };

    public static CultureInfo CurrentCulture { get; private set; } = NormalizeCulture(CultureInfo.CurrentUICulture);

    public static void SetCulture(CultureInfo? culture)
    {
        CurrentCulture = NormalizeCulture(culture);
    }

    public static bool TrySetCulture(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        switch (languageCode.Trim().ToLowerInvariant())
        {
            case "cs":
            case "cs-cz":
                SetCulture(CzechCulture);
                return true;
            case "en":
            case "en-us":
                SetCulture(EnglishCulture);
                return true;
            default:
                return false;
        }
    }

    public static string Get(TextKey key)
        => Lookup(CurrentCulture, key);

    public static string Format(TextKey key, params object[] args)
        => string.Format(CurrentCulture, Lookup(CurrentCulture, key), args);

    public static IReadOnlyList<CultureInfo> GetSupportedCultures()
        => [EnglishCulture, CzechCulture];

    internal static CultureInfo NormalizeCulture(CultureInfo? culture)
    {
        if (culture?.TwoLetterISOLanguageName.Equals("cs", StringComparison.OrdinalIgnoreCase) == true)
        {
            return CzechCulture;
        }

        return EnglishCulture;
    }

    private static string Lookup(CultureInfo culture, TextKey key)
    {
        var table = culture.TwoLetterISOLanguageName.Equals("cs", StringComparison.OrdinalIgnoreCase)
            ? Czech
            : English;
        return table.TryGetValue(key, out var value) ? value : English[key];
    }
}
