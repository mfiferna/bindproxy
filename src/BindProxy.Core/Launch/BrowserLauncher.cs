using System.Diagnostics;
using System.Text;
using BindProxy.Core.Browsers;
using BindProxy.Core.Localization;

namespace BindProxy.Core.Launch;

public static class BrowserLauncher
{
    /// <summary>Starts the browser through the session's proxy. Returns the process id.</summary>
    public static int Launch(BrowserInfo browser, int proxyPort, string nicId, ProfileMode profileMode = ProfileMode.Isolated)
    {
        EnsureLaunchAllowed(browser, profileMode);
        string? profileDir = profileMode == ProfileMode.Isolated ? GetProfileDir(browser.Name, nicId) : null;
        var psi = new ProcessStartInfo(browser.ExePath) { UseShellExecute = false };
        foreach (var arg in BuildArguments(proxyPort, profileDir))
        {
            psi.ArgumentList.Add(arg);
        }
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException(Localizer.Format(TextKey.FailedToStartBrowser, browser.Name));
        return process.Id;
    }

    public static void EnsureLaunchAllowed(BrowserInfo browser, ProfileMode profileMode)
    {
        if (profileMode != ProfileMode.UserDefault)
        {
            return;
        }

        if (IsBrowserRunning(browser))
        {
            throw new InvalidOperationException(Localizer.Format(TextKey.BrowserAlreadyRunning, browser.Name));
        }
    }

    public static bool IsBrowserRunning(BrowserInfo browser)
    {
        string processName = Path.GetFileNameWithoutExtension(browser.ExePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? fileName = process.MainModule?.FileName;
                if (string.Equals(fileName, browser.ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return true;
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
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
