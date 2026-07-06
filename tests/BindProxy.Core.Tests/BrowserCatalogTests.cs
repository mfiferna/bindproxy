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
