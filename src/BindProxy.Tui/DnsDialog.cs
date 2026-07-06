using System.Net;
using BindProxy.Core.Localization;
using BindProxy.Core.Nics;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using IApplication = Terminal.Gui.App.IApplication;

namespace BindProxy.Tui;

internal static class DnsDialog
{
    public static DnsDialogResult Prompt(IApplication app, NicInfo nic, IPAddress? current)
    {
        while (true)
        {
            var dialog = new Dialog { Title = Localizer.Format(TextKey.DnsDialogTitle, nic.Name) };
            var prompt = new Label
            {
                Text = Localizer.Get(TextKey.DnsDialogPrompt),
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
            };

            var valueField = new TextField
            {
                X = 0,
                Y = 1,
                Width = 30,
                Text = current?.ToString() ?? string.Empty,
            };

            dialog.Add(prompt, valueField);
            dialog.AddButton(new Button { Title = Localizer.Get(TextKey.Cancel) });
            dialog.AddButton(new Button { Title = Localizer.Get(TextKey.Apply) });

            app.Run(dialog);

            if (dialog.Canceled)
            {
                return new DnsDialogResult(false, current);
            }

            var text = valueField.Text.Trim();
            if (text.Length == 0)
            {
                return new DnsDialogResult(true, null);
            }

            if (IPAddress.TryParse(text, out var address))
            {
                return new DnsDialogResult(true, address);
            }

            MessageBox.ErrorQuery(app, Localizer.Get(TextKey.InvalidDnsTitle), Localizer.Format(TextKey.InvalidDnsMessage, text), Localizer.Get(TextKey.Ok));
        }
    }
}

internal readonly record struct DnsDialogResult(bool Applied, IPAddress? Override);
