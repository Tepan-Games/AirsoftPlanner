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

    private static readonly FilePickerFileType ImageFileType = new("Image (PNG, JPEG)")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg"],
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

    public Task<string?> PickExistingOperationFileAsync() => PickFileAsync("Ouvrir une OP", OperationFileType);

    public Task<string?> PickImageFileAsync() => PickFileAsync("Importer une image du terrain", ImageFileType);

    public async Task<SaveChoice> AskSaveChangesAsync(string fileName)
    {
        var choice = SaveChoice.Cancel;
        Window? dialog = null;
        Button MakeButton(string text, SaveChoice result)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) =>
            {
                choice = result;
                dialog!.Close();
            };
            return button;
        }

        dialog = CreateDialog("Modifications non enregistrées",
            $"Enregistrer les modifications de « {fileName} » ?",
            MakeButton("Enregistrer", SaveChoice.Save),
            MakeButton("Ne pas enregistrer", SaveChoice.Discard),
            MakeButton("Annuler", SaveChoice.Cancel));
        await dialog.ShowDialog(owner);
        return choice;
    }

    public async Task ShowErrorAsync(string message)
    {
        var close = new Button { Content = "OK" };
        var dialog = CreateDialog("Erreur", message, close);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    private async Task<string?> PickFileAsync(string title, FilePickerFileType type)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type],
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private static Window CreateDialog(string title, string message, params Button[] buttons)
    {
        var buttonBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttonBar.Children.AddRange(buttons);
        return new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttonBar },
            },
        };
    }
}
