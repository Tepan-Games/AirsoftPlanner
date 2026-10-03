using AirsoftPlanner.App.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AirsoftPlanner.App.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
    }

    // Les missions ou les règles ont pu changer dans un autre onglet : l'état des packages est recalculé à l'affichage.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as DocumentsViewModel)?.Refresh();
    }
}
