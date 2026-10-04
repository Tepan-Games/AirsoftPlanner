using Avalonia.Controls;

namespace AirsoftPlanner.App.Views;

public partial class RetexView : UserControl
{
    public RetexView()
    {
        InitializeComponent();
        // Bilan recalculé à chaque affichage de l'onglet.
        AttachedToVisualTree += (_, _) => (DataContext as ViewModels.RetexViewModel)?.Refresh();
    }
}
