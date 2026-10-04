using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AirsoftPlanner.App.Views;

public partial class TerrainView : UserControl
{
    public TerrainView()
    {
        InitializeComponent();
    }

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => Map.ResetView();
}
