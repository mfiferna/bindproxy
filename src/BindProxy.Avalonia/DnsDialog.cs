using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BindProxy.Core.Localization;
using BindProxy.Core.Nics;

namespace BindProxy.Avalonia;

internal sealed class DnsDialog : Window
{
    private readonly TextBox _input;
    private readonly TextBlock _errorText;
    private DnsDialogResult _result = new(false, null);

    private DnsDialog(NicInfo nic, IPAddress? current)
    {
        Title = Localizer.Format(TextKey.DnsDialogTitle, nic.Name);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        _input = new TextBox
        {
            Width = 280,
            Text = current?.ToString() ?? string.Empty,
        };
        _errorText = new TextBlock();

        var applyButton = new Button
        {
            Content = Localizer.Get(TextKey.Apply),
            MinWidth = 80,
        };
        applyButton.Click += (_, _) => Apply();

        var cancelButton = new Button
        {
            Content = Localizer.Get(TextKey.Cancel),
            MinWidth = 80,
        };
        cancelButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Localizer.Get(TextKey.DnsDialogPrompt) },
                _input,
                _errorText,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, applyButton },
                },
            },
        };
    }

    public static async Task<DnsDialogResult> ShowAsync(Window owner, NicInfo nic, IPAddress? current)
    {
        var dialog = new DnsDialog(nic, current);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void Apply()
    {
        var text = _input.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            _result = new DnsDialogResult(true, null);
            Close();
            return;
        }

        if (IPAddress.TryParse(text, out var address))
        {
            _result = new DnsDialogResult(true, address);
            Close();
            return;
        }

        _errorText.Text = Localizer.Format(TextKey.InvalidDnsMessage, text);
    }
}

internal readonly record struct DnsDialogResult(bool Applied, IPAddress? Override);
