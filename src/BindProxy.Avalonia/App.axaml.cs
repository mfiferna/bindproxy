using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BindProxy.Core.Browsers;
using BindProxy.Core.Sessions;

namespace BindProxy.Avalonia;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var browserCatalog = new BrowserCatalog(new WindowsRegistryReader());
            var sessionManager = new SessionManager();

            desktop.MainWindow = new MainWindow(browserCatalog, sessionManager);
            desktop.Exit += (_, _) => sessionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
