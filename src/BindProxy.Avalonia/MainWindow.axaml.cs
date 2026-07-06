using System.Net;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using BindProxy.Core.Browsers;
using BindProxy.Core.Launch;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;

namespace BindProxy.Avalonia;

public partial class MainWindow : Window
{
    private readonly BrowserCatalog _browserCatalog;
    private readonly SessionManager _sessionManager;
    private readonly Dictionary<string, Action> _sessionSubscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPAddress> _dnsOverrides = new(StringComparer.Ordinal);
    private readonly TextBlock _summaryText;
    private readonly StackPanel _rowsPanel;
    private IReadOnlyList<NicInfo> _currentNics = [];
    private IReadOnlyList<BrowserInfo> _currentBrowsers = [];

    public MainWindow(BrowserCatalog browserCatalog, SessionManager sessionManager)
    {
        _browserCatalog = browserCatalog;
        _sessionManager = sessionManager;

        InitializeComponent();

        _summaryText = this.FindControl<TextBlock>("SummaryText");
        _rowsPanel = this.FindControl<StackPanel>("RowsPanel");
        this.FindControl<Button>("RefreshButton").Click += (_, _) => Reload();

        _sessionManager.SessionsChanged += OnSessionsChanged;
        Closed += (_, _) =>
        {
            _sessionManager.SessionsChanged -= OnSessionsChanged;
            ClearSessionSubscriptions();
        };

        Reload();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Reload()
    {
        _currentBrowsers = _browserCatalog.GetChromiumBrowsers();
        _currentNics = NicCatalog.GetUsableNics();
        SyncSessionSubscriptions();
        RefreshRows();
    }

    private void OnSessionsChanged() => Dispatcher.UIThread.Post(() =>
    {
        SyncSessionSubscriptions();
        RefreshRows();
    });

    private void SyncSessionSubscriptions()
    {
        var activeIds = _sessionManager.Sessions.Select(session => session.Nic.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var removedId in _sessionSubscriptions.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _sessionSubscriptions[removedId]();
            _sessionSubscriptions.Remove(removedId);
        }

        foreach (var session in _sessionManager.Sessions)
        {
            if (_sessionSubscriptions.ContainsKey(session.Nic.Id))
            {
                continue;
            }

            void Handler() => Dispatcher.UIThread.Post(RefreshRows);
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

    private void RefreshRows()
    {
        _summaryText.Text = BuildSummary(_currentNics.Count, _currentBrowsers.Count);
        _rowsPanel.Children.Clear();

        foreach (var nic in _currentNics)
        {
            _rowsPanel.Children.Add(BuildNicCard(nic));
        }
    }

    private Control BuildNicCard(NicInfo nic)
    {
        var session = _sessionManager.GetSession(nic.Id);
        var dnsOverride = session?.DnsOverride ?? GetDnsOverride(nic.Id);

        var root = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            ColumnSpacing = 16,
        };

        var info = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = nic.Name,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock { Text = nic.Description, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = $"IPv4: {nic.Ipv4Address}" },
                new TextBlock { Text = $"DNS: {FormatDns(dnsOverride, nic.DnsServers)}", TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = FormatStatus(session, dnsOverride),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = session?.LastError is null ? Brushes.DarkSlateGray : Brushes.IndianRed,
                    Margin = new Thickness(0, 6, 0, 0),
                },
            },
        };

        var actions = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        if (_currentBrowsers.Count == 0)
        {
            actions.Children.Add(new TextBlock
            {
                Text = "No Chromium browsers detected",
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }
        else
        {
            var browserPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                ItemSpacing = 8,
                LineSpacing = 8,
            };

            foreach (var browser in _currentBrowsers)
            {
                var button = new Button { Content = browser.Name };
                button.Click += async (_, _) => await LaunchBrowserAsync(nic, browser);
                browserPanel.Children.Add(button);
            }

            actions.Children.Add(browserPanel);
        }

        var utilityPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            ItemSpacing = 8,
            LineSpacing = 8,
        };

        var manualButton = new Button { Content = "Manual" };
        manualButton.Click += async (_, _) => await StartManualAsync(nic);

        var dnsButton = new Button { Content = "DNS" };
        dnsButton.Click += async (_, _) => await EditDnsAsync(nic);

        var stopButton = new Button
        {
            Content = "Stop",
            IsEnabled = session is not null,
        };
        stopButton.Click += async (_, _) => await StopAsync(nic);

        utilityPanel.Children.Add(manualButton);
        utilityPanel.Children.Add(dnsButton);
        utilityPanel.Children.Add(stopButton);
        actions.Children.Add(utilityPanel);

        Grid.SetColumn(info, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(info);
        grid.Children.Add(actions);
        root.Child = grid;

        return root;
    }

    private async Task LaunchBrowserAsync(NicInfo nic, BrowserInfo browser)
    {
        try
        {
            var session = _sessionManager.GetOrStart(nic, GetDnsOverride(nic.Id));
            SyncSessionSubscriptions();
            int pid = BrowserLauncher.Launch(browser, session.Port, nic.Id);
            session.AddLaunchedProcess(pid);
            RefreshRows();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Launch failed", ex.Message);
        }
    }

    private Task StartManualAsync(NicInfo nic)
    {
        try
        {
            _sessionManager.GetOrStart(nic, GetDnsOverride(nic.Id));
            SyncSessionSubscriptions();
            RefreshRows();
        }
        catch (Exception ex)
        {
            return MessageDialog.ShowAsync(this, "Failed to start proxy", ex.Message);
        }

        return Task.CompletedTask;
    }

    private async Task StopAsync(NicInfo nic)
    {
        try
        {
            await _sessionManager.StopAsync(nic.Id).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(RefreshRows);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ShowAsync(this, "Failed to stop proxy", ex.Message));
        }
    }

    private async Task EditDnsAsync(NicInfo nic)
    {
        var current = _sessionManager.GetSession(nic.Id)?.DnsOverride ?? GetDnsOverride(nic.Id);
        var result = await DnsDialog.ShowAsync(this, nic, current);
        if (!result.Applied)
        {
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
        RefreshRows();
    }

    private IPAddress? GetDnsOverride(string nicId)
        => _dnsOverrides.TryGetValue(nicId, out var dns) ? dns : null;

    private static string BuildSummary(int nicCount, int browserCount)
    {
        if (nicCount == 0)
        {
            return "No default-usable IPv4 NICs detected.";
        }

        if (browserCount == 0)
        {
            return "No Chromium browsers detected. Manual proxy start is still available.";
        }

        return "Choose a NIC, then launch a browser, start the proxy manually, or set a DNS override.";
    }

    private static string FormatDns(IPAddress? dnsOverride, IReadOnlyList<IPAddress> defaults)
    {
        if (dnsOverride is not null)
        {
            return $"{dnsOverride} (override)";
        }

        return defaults.Count == 0 ? "none" : string.Join(", ", defaults);
    }

    private static string FormatStatus(ProxySession? session, IPAddress? pendingDns)
    {
        if (session is null)
        {
            return pendingDns is null ? "stopped" : $"stopped · pending DNS {pendingDns}";
        }

        var parts = new List<string>
        {
            $"proxy {session.ProxyUrl}",
            $"{session.LaunchedProcessIds.Count} browsers",
            $"{session.ActiveConnections} conns",
        };

        if (!string.IsNullOrWhiteSpace(session.LastError))
        {
            parts.Add($"error: {session.LastError}");
        }

        return string.Join(" · ", parts);
    }
}
