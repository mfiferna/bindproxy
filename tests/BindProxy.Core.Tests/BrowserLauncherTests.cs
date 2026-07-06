using BindProxy.Core.Launch;
using Xunit;

namespace BindProxy.Core.Tests;

public class BrowserLauncherTests
{
    [Fact]
    public void Builds_arguments_with_isolated_profile()
    {
        var args = BrowserLauncher.BuildArguments(8080, @"C:\profiles\chrome-nic1");
        Assert.Equal(new[]
        {
            "--proxy-server=http://127.0.0.1:8080",
            "--proxy-bypass-list=<-loopback>",
            @"--user-data-dir=C:\profiles\chrome-nic1",
            "--no-first-run",
            "--no-default-browser-check",
        }, args);
    }

    [Fact]
    public void Omits_user_data_dir_when_profile_dir_is_null()
    {
        var args = BrowserLauncher.BuildArguments(8080, null);
        Assert.DoesNotContain(args, a => a.StartsWith("--user-data-dir"));
        Assert.Contains("--proxy-server=http://127.0.0.1:8080", args);
    }

    [Fact]
    public void Profile_dir_is_sanitized_and_under_local_appdata()
    {
        var dir = BrowserLauncher.GetProfileDir("Google Chrome", "{B2AA02F3-FF44-4E52-A}");
        Assert.Contains(@"BindProxy\Profiles", dir);
        var leaf = Path.GetFileName(dir);
        Assert.Matches("^[A-Za-z0-9._-]+$", leaf);
        Assert.Contains("Google_Chrome", leaf);
    }

    [Fact]
    public void Does_not_throw_for_isolated_profile_mode()
    {
        var browser = new BindProxy.Core.Browsers.BrowserInfo("Chrome", @"C:\Program Files\Chrome\chrome.exe");
        BrowserLauncher.EnsureLaunchAllowed(browser, ProfileMode.Isolated);
    }
}
