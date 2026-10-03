using AirsoftPlanner.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

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
}
