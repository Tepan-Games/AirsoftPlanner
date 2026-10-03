using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace AirsoftPlanner.App.Services;

public class FileDialogService(Window owner) : IFileDialogService
{
    private static readonly FilePickerFileType OperationFileType = new("Fichier d'OP Airsoft Planner")
    {
        Patterns = ["*" + OperationFile.Extension],
    };

    public async Task<string?> PickNewOperationFileAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Créer une nouvelle OP",
            SuggestedFileName = suggestedName + OperationFile.Extension,
            DefaultExtension = OperationFile.Extension,
            FileTypeChoices = [OperationFileType],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExistingOperationFileAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ouvrir une OP",
            AllowMultiple = false,
            FileTypeFilter = [OperationFileType],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task ShowErrorAsync(string message)
    {
        var close = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = "Erreur",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
