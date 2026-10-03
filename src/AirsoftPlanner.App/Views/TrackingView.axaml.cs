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

    private void OnReattachClick(object? sender, RoutedEventArgs e)
    {
        MapHost.Reattach();
        TimelineHost.Reattach();
        StatusHost.Reattach();
    }
}
