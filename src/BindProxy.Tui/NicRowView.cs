using System.Net;
using BindProxy.Core.Browsers;
using BindProxy.Core.Localization;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace BindProxy.Tui;

internal sealed class NicRowView : FrameView
{
    private readonly NicInfo _nic;
    private readonly Func<IPAddress?> _getPendingDnsOverride;
    private readonly Label _infoLabel;
    private readonly Label _statusLabel;
    private readonly Button _stopButton;

    public NicRowView(
        NicInfo nic,
        IReadOnlyList<BrowserInfo> browsers,
        Func<BrowserInfo, Task> launchBrowserAsync,
        Func<BrowserInfo, Task> launchBrowserWithDefaultProfileAsync,
        Func<Task> startManualAsync,
        Func<Task> editDnsAsync,
        Func<Task> stopAsync,
        Func<IPAddress?> getPendingDnsOverride)
    {
        _nic = nic;
        _getPendingDnsOverride = getPendingDnsOverride;

        Title = nic.Name;

        _infoLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(56),
            Height = 4,
        };

        _statusLabel = new Label
        {
            X = 0,
            Y = 4,
            Width = Dim.Fill(),
            Height = 1,
        };

        Add(_infoLabel, _statusLabel);

        View? previous = null;
        foreach (var browser in browsers)
        {
            var button = CreateButton(browser.Name, async () => await launchBrowserAsync(browser).ConfigureAwait(false));
            button.X = previous is null ? Pos.Percent(58) : Pos.Right(previous) + 1;
            button.Y = 0;
            Add(button);
            previous = button;
        }

        previous = null;
        foreach (var browser in browsers)
        {
            var button = CreateButton(Localizer.Format(TextKey.DefaultProfileButton, browser.Name), async () => await launchBrowserWithDefaultProfileAsync(browser).ConfigureAwait(false));
            button.X = previous is null ? Pos.Percent(58) : Pos.Right(previous) + 1;
            button.Y = 1;
            Add(button);
            previous = button;
        }

        var manualButton = CreateButton(Localizer.Get(TextKey.Manual), startManualAsync);
        manualButton.X = Pos.Percent(58);
        manualButton.Y = 3;

        var dnsButton = CreateButton(Localizer.Get(TextKey.Dns), editDnsAsync);
        dnsButton.X = Pos.Right(manualButton) + 1;
        dnsButton.Y = 3;

        _stopButton = CreateButton(Localizer.Get(TextKey.Stop), stopAsync);
        _stopButton.X = Pos.Right(dnsButton) + 1;
        _stopButton.Y = 3;

        Add(manualButton, dnsButton, _stopButton);

        if (browsers.Count == 0)
        {
            var noBrowsersLabel = new Label
            {
                Text = Localizer.Get(TextKey.NoChromiumFound),
                X = Pos.Percent(58),
                Y = 0,
                Width = Dim.Fill(),
                Height = 2,
            };
            Add(noBrowsersLabel);
        }

        Refresh(null);
    }

    public void Refresh(ProxySession? session)
    {
        var dnsOverride = session?.DnsOverride ?? _getPendingDnsOverride();
        _infoLabel.Text =
            $"{_nic.Ipv4Address}\n" +
            $"{_nic.Description}\n" +
            $"{Localizer.Format(TextKey.DnsLabel, FormatDns(dnsOverride, _nic.DnsServers))}\n" +
            Localizer.Get(TextKey.NicRowInstruction);

        _statusLabel.Text = FormatStatus(session, dnsOverride);
        _stopButton.Enabled = session is not null;
    }

    private static Button CreateButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Accepted += async (_, _) => await action().ConfigureAwait(false);
        return button;
    }

    private static string FormatDns(IPAddress? dnsOverride, IReadOnlyList<IPAddress> defaults)
    {
        if (dnsOverride is not null)
        {
            return Localizer.Format(TextKey.DnsOverrideSuffix, dnsOverride);
        }

        return defaults.Count == 0 ? Localizer.Get(TextKey.SystemDefaultUnavailable) : string.Join(", ", defaults);
    }

    private static string FormatStatus(ProxySession? session, IPAddress? pendingDns)
    {
        if (session is null)
        {
            return pendingDns is null ? Localizer.Get(TextKey.Stopped) : Localizer.Format(TextKey.PendingDns, pendingDns);
        }

        var parts = new List<string>
        {
            Localizer.Format(TextKey.ProxyStatus, session.ProxyUrl),
            Localizer.Format(TextKey.BrowsersCount, session.LaunchedProcessIds.Count),
            Localizer.Format(TextKey.ConnectionsCount, session.ActiveConnections),
        };

        if (!string.IsNullOrWhiteSpace(session.LastError))
        {
            parts.Add(Localizer.Format(TextKey.ErrorStatus, session.LastError));
        }

        return string.Join(" · ", parts);
    }
}
