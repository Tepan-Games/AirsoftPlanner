using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AirsoftPlanner.App.Views;

public partial class MissionsView : UserControl
{
    public MissionsView()
    {
        InitializeComponent();
    }

    private void OnFitClick(object? sender, RoutedEventArgs e) => Timeline.FitToHeight();
}
