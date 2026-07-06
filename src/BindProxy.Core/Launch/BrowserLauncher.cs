using System.Diagnostics;
using System.Text;
using BindProxy.Core.Browsers;

namespace BindProxy.Core.Launch;

public static class BrowserLauncher
{
    /// <summary>Starts the browser through the session's proxy. Returns the process id.</summary>
    public static int Launch(BrowserInfo browser, int proxyPort, string nicId, ProfileMode profileMode = ProfileMode.Isolated)
    {
        string? profileDir = profileMode == ProfileMode.Isolated ? GetProfileDir(browser.Name, nicId) : null;
        var psi = new ProcessStartInfo(browser.ExePath) { UseShellExecute = false };
        foreach (var arg in BuildArguments(proxyPort, profileDir))
        {
            psi.ArgumentList.Add(arg);
        }
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {browser.Name}");
        return process.Id;
    }

    public static IReadOnlyList<string> BuildArguments(int proxyPort, string? profileDir)
    {
        var args = new List<string>
        {
            $"--proxy-server=http://127.0.0.1:{proxyPort}",
            "--proxy-bypass-list=<-loopback>",
        };
        if (profileDir is not null)
        {
            args.Add($"--user-data-dir={profileDir}");
        }
        args.Add("--no-first-run");
        args.Add("--no-default-browser-check");
        return args;
    }

    /// <summary>Per-browser, per-NIC persistent profile directory.</summary>
    public static string GetProfileDir(string browserName, string nicId)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BindProxy", "Profiles", Sanitize($"{browserName}-{nicId}"));
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        return sb.ToString();
    }
}
