using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using BindProxy.Core.Browsers;
using BindProxy.Core.Launch;
using BindProxy.Core.Localization;
using BindProxy.Core.Nics;
using BindProxy.Core.Sessions;
using System.Globalization;

namespace BindProxy.Avalonia;

public partial class MainWindow : Window
{
    private readonly BrowserCatalog _browserCatalog;
    private readonly SessionManager _sessionManager;
    private readonly bool _ownsSessionManager;
    private readonly Dictionary<string, Action> _sessionSubscriptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPAddress> _dnsOverrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _useExistingProfile = new(StringComparer.Ordinal);
    private readonly TextBlock _titleText;
    private readonly TextBlock _summaryText;
    private readonly StackPanel _rowsPanel;
    private readonly TextBlock _languageLabel;
    private readonly ComboBox _languageComboBox;
    private IReadOnlyList<NicInfo> _currentNics = [];
    private IReadOnlyList<BrowserInfo> _currentBrowsers = [];

    public MainWindow()
        : this(new BrowserCatalog(new WindowsRegistryReader()), new SessionManager(), ownsSessionManager: true)
    {
    }

    public MainWindow(BrowserCatalog browserCatalog, SessionManager sessionManager)
        : this(browserCatalog, sessionManager, ownsSessionManager: false)
    {
    }

    private MainWindow(BrowserCatalog browserCatalog, SessionManager sessionManager, bool ownsSessionManager)
    {
        _browserCatalog = browserCatalog;
        _sessionManager = sessionManager;
        _ownsSessionManager = ownsSessionManager;

        InitializeComponent();

        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _summaryText = this.FindControl<TextBlock>("SummaryText")!;
        _rowsPanel = this.FindControl<StackPanel>("RowsPanel")!;
        _languageLabel = this.FindControl<TextBlock>("LanguageLabel")!;
        _languageComboBox = this.FindControl<ComboBox>("LanguageComboBox")!;
        var refreshButton = this.FindControl<Button>("RefreshButton")!;
        refreshButton.Click += (_, _) => Reload();
        _languageComboBox.ItemsSource = BuildLanguageOptions();
        _languageComboBox.SelectionChanged += (_, _) => OnLanguageChanged();
        ApplyLocalizedChrome();

        _sessionManager.SessionsChanged += OnSessionsChanged;
        Closed += (_, _) =>

        {
            _sessionManager.SessionsChanged -= OnSessionsChanged;
            ClearSessionSubscriptions();
            if (_ownsSessionManager)
            {
                _sessionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        };

        Reload();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void ApplyLocalizedChrome()
    {
        Title = Localizer.Get(TextKey.AppTitle);
        _titleText.Text = Localizer.Get(TextKey.AppTitle);
        _languageLabel.Text = $"{Localizer.Get(TextKey.Language)}:";
        var refreshButton = this.FindControl<Button>("RefreshButton")!;
        refreshButton.Content = Localizer.Get(TextKey.Refresh);
        ToolTip.SetTip(refreshButton, Localizer.Get(TextKey.RefreshNicAndBrowserLists));

        var options = BuildLanguageOptions();
        _languageComboBox.ItemsSource = options;
        _languageComboBox.SelectedItem = options.First(option => option.Culture.Name == Localizer.CurrentCulture.Name);
    }

    private void OnLanguageChanged()
    {
        if (_languageComboBox.SelectedItem is not LanguageOption option)
        {
            return;
        }

        if (option.Culture.Name == Localizer.CurrentCulture.Name)
        {
            return;
        }

        Localizer.SetCulture(option.Culture);
        ApplyLocalizedChrome();
        RefreshRows();
    }

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
        var palette = Palette.Resolve(ActualThemeVariant == ThemeVariant.Dark);

        var card = new Border
        {
            Background = palette.CardBackground,
            BorderBrush = palette.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 16),
        };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(BuildHeaderRow(nic, session, dnsOverride, palette));

        if (_currentBrowsers.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = Localizer.Get(TextKey.NoBrowsersDetected),
                TextWrapping = TextWrapping.Wrap,
                Foreground = palette.MutedText,
                FontSize = 13,
            });
        }
        else
        {
            body.Children.Add(BuildBrowserRow(nic, palette));
        }

        body.Children.Add(BuildSecondaryRow(nic, session, palette));

        if (session is not null)
        {
            body.Children.Add(BuildSessionDetailRow(session, palette));
        }

        card.Child = body;
        return card;
    }

    private Control BuildHeaderRow(NicInfo nic, ProxySession? session, IPAddress? dnsOverride, Palette palette)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };

        var icon = ConnectionIcon.Create(nic.Kind, palette.Accent);
        Grid.SetColumn(icon, 0);

        var identity = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new TextBlock
        {
            Text = nic.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        identity.Children.Add(new TextBlock
        {
            Text = $"{KindLabel(nic.Kind)} · {Localizer.Format(TextKey.ConnectionAddressLine, nic.Ipv4Address, FormatDns(dnsOverride, nic.DnsServers))}",
            FontSize = 12,
            Foreground = palette.MutedText,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(identity, 1);

        var pill = BuildStatusPill(session, palette);
        Grid.SetColumn(pill, 2);
        pill.VerticalAlignment = VerticalAlignment.Center;

        grid.Children.Add(icon);
        grid.Children.Add(identity);
        grid.Children.Add(pill);
        return grid;
    }

    private static Control BuildStatusPill(ProxySession? session, Palette palette)
    {
        var (background, foreground, text) = session switch
        {
            { LastError.Length: > 0 } => (palette.ErrorBackground, palette.ErrorForeground, Localizer.Get(TextKey.StatusErrorPill)),
            not null => (palette.RunningBackground, palette.RunningForeground, Localizer.Get(TextKey.StatusRunningPill)),
            null => (palette.StoppedBackground, palette.StoppedForeground, Localizer.Get(TextKey.StatusStoppedPill)),
        };

        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };

        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { dot, label },
            },
        };
    }

    private Control BuildBrowserRow(NicInfo nic, Palette palette)
    {
        var stack = new StackPanel { Spacing = 8 };

        var browserButtons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var browser in _currentBrowsers)
        {
            var button = new Button
            {
                Content = Localizer.Format(TextKey.OpenWithBrowser, browser.Name),
                Background = palette.Accent,
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 8),
                Margin = new Thickness(0, 0, 8, 8),
            };
            button.Click += async (_, _) => await LaunchBrowserAsync(nic, browser, CurrentProfileMode(nic.Id));
            browserButtons.Children.Add(button);
        }

        stack.Children.Add(browserButtons);

        var useExisting = new CheckBox
        {
            Content = Localizer.Get(TextKey.UseExistingProfileCheckbox),
            FontSize = 12,
            IsChecked = _useExistingProfile.TryGetValue(nic.Id, out var isChecked) && isChecked,
        };
        ToolTip.SetTip(useExisting, Localizer.Get(TextKey.UseExistingProfileTooltip));
        useExisting.IsCheckedChanged += (_, _) => _useExistingProfile[nic.Id] = useExisting.IsChecked ?? false;
        stack.Children.Add(useExisting);

        return stack;
    }

    private ProfileMode CurrentProfileMode(string nicId)
        => _useExistingProfile.TryGetValue(nicId, out var useExisting) && useExisting
            ? ProfileMode.UserDefault
            : ProfileMode.Isolated;

    private Control BuildSecondaryRow(NicInfo nic, ProxySession? session, Palette palette)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };

        var manualButton = new Button { Content = Localizer.Get(TextKey.StartProxyOnly), Margin = new Thickness(0, 0, 6, 0) };
        manualButton.Click += async (_, _) => await StartManualAsync(nic);

        var dnsButton = new Button { Content = Localizer.Get(TextKey.ChangeDns), Margin = new Thickness(0, 0, 6, 0) };
        dnsButton.Click += async (_, _) => await EditDnsAsync(nic);

        var stopButton = new Button
        {
            Content = Localizer.Get(TextKey.StopConnection),
            IsEnabled = session is not null,
        };
        stopButton.Click += async (_, _) => await StopAsync(nic);

        foreach (var button in new[] { manualButton, dnsButton, stopButton })
        {
            button.FontSize = 12;
            button.Foreground = palette.MutedText;
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(6, 4);
        }

        panel.Children.Add(manualButton);
        panel.Children.Add(dnsButton);
        panel.Children.Add(stopButton);
        return panel;
    }

    private static Control BuildSessionDetailRow(ProxySession session, Palette palette)
    {
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = $"{Localizer.Format(TextKey.ProxyAddressLine, session.ProxyUrl)} · {Localizer.Format(TextKey.ActiveBrowsersAndConnections, session.LaunchedProcessIds.Count, session.ActiveConnections)}",
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            FontSize = 12,
            Foreground = palette.MutedText,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(session.LastError))
        {
            stack.Children.Add(new TextBlock
            {
                Text = Localizer.Format(TextKey.LastErrorLine, session.LastError),
                FontSize = 12,
                Foreground = palette.ErrorForeground,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return stack;
    }

    private static string KindLabel(NicKind kind) => kind switch
    {
        NicKind.Ethernet => Localizer.Get(TextKey.NicKindEthernet),
        NicKind.Wireless => Localizer.Get(TextKey.NicKindWireless),
        _ => Localizer.Get(TextKey.NicKindOtherConnection),
    };

    private async Task LaunchBrowserAsync(NicInfo nic, BrowserInfo browser, ProfileMode profileMode = ProfileMode.Isolated)
    {
        try
        {
            var session = _sessionManager.GetOrStart(nic, GetDnsOverride(nic.Id));
            SyncSessionSubscriptions();
            int pid = BrowserLauncher.Launch(browser, session.Port, nic.Id, profileMode);
            session.AddLaunchedProcess(pid);
            RefreshRows();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, Localizer.Get(TextKey.LaunchFailedTitle), ex.Message);
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
            return MessageDialog.ShowAsync(this, Localizer.Get(TextKey.StartProxyFailedTitle), ex.Message);
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
            await Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ShowAsync(this, Localizer.Get(TextKey.StopProxyFailedTitle), ex.Message));
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
            return Localizer.Get(TextKey.NoUsableNicsDetected);
        }

        if (browserCount == 0)
        {
            return Localizer.Get(TextKey.NoChromiumDetectedManualAvailable);
        }

        return Localizer.Get(TextKey.AppSubtitle);
    }

    private static string FormatDns(IPAddress? dnsOverride, IReadOnlyList<IPAddress> defaults)
    {
        if (dnsOverride is not null)
        {
            return Localizer.Format(TextKey.DnsCustomSuffix, dnsOverride);
        }

        return defaults.Count == 0 ? Localizer.Get(TextKey.DnsUnavailable) : string.Join(", ", defaults);
    }

    private static IReadOnlyList<LanguageOption> BuildLanguageOptions()
    {
        return
        [
            new LanguageOption(CultureInfo.GetCultureInfo("en-US"), Localizer.Get(TextKey.EnglishLanguage)),
            new LanguageOption(CultureInfo.GetCultureInfo("cs-CZ"), Localizer.Get(TextKey.CzechLanguage)),
        ];
    }

    private sealed record LanguageOption(CultureInfo Culture, string Label)
    {
        public override string ToString() => Label;
    }
}
