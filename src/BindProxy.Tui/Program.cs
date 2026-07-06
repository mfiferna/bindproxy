using BindProxy.Core.Browsers;
using BindProxy.Core.Sessions;
using Terminal.Gui.App;

namespace BindProxy.Tui;

public static class Program
{
    public static void Main()
    {
        var registry = new WindowsRegistryReader();
        var browserCatalog = new BrowserCatalog(registry);
        var sessionManager = new SessionManager();
        using var app = Application.Create().Init();

        try
        {
            var mainWindow = new MainWindow(app, browserCatalog, sessionManager);
            app.Run(mainWindow);
        }
        finally
        {
            sessionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
