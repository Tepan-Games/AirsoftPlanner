using AirsoftPlanner.App.ViewModels;
using Avalonia.Controls;

namespace AirsoftPlanner.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainViewModel viewModel)
            return;

        // La fermeture est annulée le temps de demander s'il faut enregistrer, puis relancée.
        e.Cancel = true;
        if (await viewModel.ConfirmDiscardOrSaveAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
