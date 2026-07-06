using System.Net;
using BindProxy.Core.Browsers;
using BindProxy.Core.Launch;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using IApplication = Terminal.Gui.App.IApplication;

namespace BindProxy.Tui;

internal sealed class MainWindow : Window
{
    private readonly IApplication _app;
    private readonly BrowserCatalog _browserCatalog;
    private readonly SessionManager _sessionManager;
    private readonly Dictionary<string, NicRowView> _rowsByNicId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _sessionSubscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPAddress> _dnsOverrides = new(StringComparer.Ordinal);
    private readonly View _rowsHost;
    private readonly Label _noteLabel;

    public MainWindow(IApplication app, BrowserCatalog browserCatalog, SessionManager sessionManager)
    {
        _app = app;
        _browserCatalog = browserCatalog;
        _sessionManager = sessionManager;

        Title = "BindProxy";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _noteLabel = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 2,
        };

        _rowsHost = new View
        {
            X = 0,
            Y = Pos.Bottom(_noteLabel),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        var statusBar = new StatusBar(
        [
            new Shortcut(new Key(KeyCode.CtrlMask | KeyCode.Q), "Quit", () => _app.RequestStop(this), "Exit BindProxy"),
            new Shortcut(new Key(KeyCode.F5), "Refresh", Reload, "Refresh NIC and browser lists"),
        ])
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
        };

        Add(_noteLabel, _rowsHost, statusBar);

        _sessionManager.SessionsChanged += OnSessionsChanged;
        Disposing += (_, _) =>
        {
            _sessionManager.SessionsChanged -= OnSessionsChanged;
            ClearSessionSubscriptions();
        };

        Reload();
    }

    private void Reload()
    {
        var browsers = _browserCatalog.GetChromiumBrowsers();
        var nics = NicCatalog.GetUsableNics();

        SyncSessionSubscriptions();
        RebuildRows(nics, browsers);
        RefreshAllRows();
    }

    private void RebuildRows(IReadOnlyList<NicInfo> nics, IReadOnlyList<BrowserInfo> browsers)
    {
        foreach (var row in _rowsByNicId.Values)
        {
            _rowsHost.Remove(row);
        }
        _rowsByNicId.Clear();

        _noteLabel.Text = BuildNote(nics.Count, browsers.Count);

        var rowY = Pos.Absolute(0);
        foreach (var nic in nics)
        {
            var row = new NicRowView(
                nic,
                browsers,
                launchBrowserAsync: browser => LaunchBrowserAsync(nic, browser),
                startManualAsync: () => StartManualAsync(nic),
                editDnsAsync: () => EditDnsAsync(nic),
                stopAsync: () => StopAsync(nic),
                getPendingDnsOverride: () => GetDnsOverride(nic.Id));

            row.X = 0;
            row.Y = rowY;
            row.Width = Dim.Fill();
            row.Height = 6;

            _rowsHost.Add(row);
            _rowsByNicId.Add(nic.Id, row);
            rowY += 6;
        }
    }

    private static string BuildNote(int nicCount, int browserCount)
    {
        if (nicCount == 0)
        {
            return "No usable IPv4 NICs detected.";
        }

        if (browserCount == 0)
        {
            return "No Chromium browsers detected. Manual proxy start is still available.";
        }

        return "Choose a NIC row, then launch a browser or start the proxy manually.";
    }

    private void OnSessionsChanged() => _app.Invoke(() =>
    {
        SyncSessionSubscriptions();
        RefreshAllRows();
    });

    private void SyncSessionSubscriptions()
    {
        var activeIds = _sessionManager.Sessions.Select(session => session.Nic.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var removedId in _sessionSubscriptions.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _sessionSubscriptions.Remove(removedId);
        }

        foreach (var session in _sessionManager.Sessions)
        {
            if (_sessionSubscriptions.ContainsKey(session.Nic.Id))
            {
                continue;
            }

            void Handler() => _app.Invoke(() => RefreshRow(session.Nic.Id));
            session.Changed += Handler;
            _sessionSubscriptions.Add(session.Nic.Id, () => session.Changed -= Handler);
        }
    }

    private void ClearSessionSubscriptions()
    {
        foreach (var unsubscribe in _sessionSubscriptions.Values)
        {
            unsubscribe();
        }
        _sessionSubscriptions.Clear();
    }

    private void RefreshAllRows()
    {
        foreach (var nicId in _rowsByNicId.Keys)
        {
            RefreshRow(nicId);
        }
    }

    private void RefreshRow(string nicId)
    {
        if (_rowsByNicId.TryGetValue(nicId, out var row))
        {
            row.Refresh(_sessionManager.GetSession(nicId));
        }
    }

    private async Task LaunchBrowserAsync(NicInfo nic, BrowserInfo browser)
    {
        try
        {
            var session = _sessionManager.GetOrStart(nic, GetDnsOverride(nic.Id));
            SyncSessionSubscriptions();
            int pid = BrowserLauncher.Launch(browser, session.Port, nic.Id);
            session.AddLaunchedProcess(pid);
            RefreshRow(nic.Id);
        }
        catch (Exception ex)
        {
            ShowError("Launch failed", ex.Message);
        }

        await Task.CompletedTask;
    }

    private async Task StartManualAsync(NicInfo nic)
    {
        try
        {
            _sessionManager.GetOrStart(nic, GetDnsOverride(nic.Id));
            SyncSessionSubscriptions();
            RefreshRow(nic.Id);
        }
        catch (Exception ex)
        {
            ShowError("Failed to start proxy", ex.Message);
        }

        await Task.CompletedTask;
    }

    private async Task StopAsync(NicInfo nic)
    {
        try
        {
            await _sessionManager.StopAsync(nic.Id).ConfigureAwait(false);
            _app.Invoke(() => RefreshRow(nic.Id));
        }
        catch (Exception ex)
        {
            _app.Invoke(() => ShowError("Failed to stop proxy", ex.Message));
        }
    }

    private async Task EditDnsAsync(NicInfo nic)
    {
        var current = _sessionManager.GetSession(nic.Id)?.DnsOverride ?? GetDnsOverride(nic.Id);
        var result = DnsDialog.Prompt(_app, nic, current);
        if (!result.Applied)
        {
            await Task.CompletedTask;
            return;
        }

        if (result.Override is null)
        {
            _dnsOverrides.Remove(nic.Id);
        }
        else
        {
            _dnsOverrides[nic.Id] = result.Override;
        }

        var session = _sessionManager.GetSession(nic.Id);
        session?.SetDnsOverride(result.Override);
        RefreshRow(nic.Id);

        await Task.CompletedTask;
    }

    private IPAddress? GetDnsOverride(string nicId)
        => _dnsOverrides.TryGetValue(nicId, out var dns) ? dns : null;

    private void ShowError(string title, string message)
        => MessageBox.ErrorQuery(_app, title, message, "OK");
}
