using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BindProxy.Core.Localization;

namespace BindProxy.Avalonia;

internal sealed class MessageDialog : Window
{
    private MessageDialog(string title, string message)
    {
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var okButton = new Button
        {
            Content = Localizer.Get(TextKey.Ok),
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        okButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Width = 420,
                    TextWrapping = TextWrapping.Wrap,
                },
                okButton,
            },
        };
    }

    public static Task ShowAsync(Window owner, string title, string message)
        => new MessageDialog(title, message).ShowDialog(owner);
}
