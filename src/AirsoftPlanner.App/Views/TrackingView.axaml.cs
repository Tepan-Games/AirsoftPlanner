using AirsoftPlanner.App.Controls;
using AirsoftPlanner.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AirsoftPlanner.App.Views;

public partial class TrackingView : UserControl
{
    public TrackingView()
    {
        InitializeComponent();
    }

    // Les fréquences ont pu changer dans les onglets Factions ou Équipes.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as TrackingViewModel)?.RefreshRadioPlan();
    }

    private void OnExplodeClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            DetachableHost.Explode(owner, [MapHost, TimelineHost, StatusHost]);
    }

    private TrackingSettingsWindow? _settings;

    /// <summary>Paramètres du suivi (réception des positions), dans une fenêtre à part.</summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new TrackingSettingsWindow { DataContext = (DataContext as ViewModels.TrackingViewModel)?.Gps };
        if (TopLevel.GetTopLevel(this) is Window owner)
            _settings.Show(owner);
        else
            _settings.Show();
    }

    private void OnReattachClick(object? sender, RoutedEventArgs e)
    {
        MapHost.Reattach();
        TimelineHost.Reattach();
        StatusHost.Reattach();
    }
}
