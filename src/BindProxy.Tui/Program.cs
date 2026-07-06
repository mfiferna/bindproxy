using BindProxy.Core.Browsers;
using BindProxy.Core.Localization;
using BindProxy.Core.Sessions;
using Terminal.Gui.App;

namespace BindProxy.Tui;

public static class Program
{
    public static void Main(string[] args)
    {
        ApplyLanguageArgument(args);
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

    private static void ApplyLanguageArgument(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--lang", StringComparison.OrdinalIgnoreCase) || arg.Equals("--language", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || !Localizer.TrySetCulture(args[i + 1]))
                {
                    throw new InvalidOperationException("Expected '--lang en' or '--lang cs'.");
                }
                return;
            }

            const string langPrefix = "--lang=";
            const string languagePrefix = "--language=";
            if (arg.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!Localizer.TrySetCulture(arg[langPrefix.Length..]))
                {
                    throw new InvalidOperationException("Expected '--lang en' or '--lang cs'.");
                }
                return;
            }

            if (arg.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!Localizer.TrySetCulture(arg[languagePrefix.Length..]))
                {
                    throw new InvalidOperationException("Expected '--lang en' or '--lang cs'.");
                }
                return;
            }
        }
    }
}
