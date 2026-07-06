using Avalonia.Controls;
using Avalonia.Layout;

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
            Content = "OK",
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
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                okButton,
            },
        };
    }

    public static Task ShowAsync(Window owner, string title, string message)
        => new MessageDialog(title, message).ShowDialog(owner);
}
