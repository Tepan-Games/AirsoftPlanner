using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.App.ViewModels;
using AirsoftPlanner.App.Views;

namespace AirsoftPlanner.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppSettings.ApplyTheme();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { Args: { } arguments }
            && arguments.Contains("--serveur-gps", System.StringComparer.OrdinalIgnoreCase))
            AppSettings.StartGpsServerOnce = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainViewModel(new FileDialogService(window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => viewModel.Dispose();
            if (desktop.Args?.FirstOrDefault(a => !a.StartsWith("--")) is { } path)
                window.Opened += async (_, _) => await viewModel.OpenFileAsync(path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}