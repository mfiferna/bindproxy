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
