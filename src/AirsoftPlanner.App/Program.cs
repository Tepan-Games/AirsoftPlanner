using Avalonia;
using System;

namespace AirsoftPlanner.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Langue choisie dans le logiciel, sinon celle de Windows (français par défaut).
        var language = AppSettings.Current.Language;
        Core.Localization.L.SetLanguage(string.IsNullOrEmpty(language) ? Core.Localization.L.SystemLanguage() : language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
