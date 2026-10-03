using System.Collections.Generic;
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

    private static readonly FilePickerFileType DocumentFileType = new("Document (PDF, Word, OpenDocument, texte)")
    {
        Patterns = ["*.pdf", "*.docx", "*.doc", "*.odt", "*.txt", "*.md"],
    };

    public Task<string?> PickOpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns) =>
        PickFileAsync(title, new FilePickerFileType(typeName) { Patterns = patterns });

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string extension)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*" + extension] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        var close = new Button { Content = "OK" };
        var dialog = CreateDialog(title, message, close);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public async Task ShowImageAsync(string title, string message, byte[] png)
    {
        var close = new Button { Content = "Fermer", HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new Image { Source = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(png)), Width = 320, Height = 320 },
                    new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    close,
                },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public Task<string?> PickDocumentFileAsync() => PickFileAsync("Importer un document de règles", DocumentFileType);

    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        var start = startFolder is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(startFolder);
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

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
