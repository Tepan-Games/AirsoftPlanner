using System.Collections.Generic;
using System.Threading.Tasks;

namespace AirsoftPlanner.App.Services;

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

public interface IFileDialogService
{
    Task<string?> PickNewOperationFileAsync(string suggestedName);

    Task<string?> PickExistingOperationFileAsync();

    Task<string?> PickImageFileAsync();

    /// <summary>Document de règles à importer (PDF, Word, OpenDocument, texte).</summary>
    Task<string?> PickDocumentFileAsync();

    Task<string?> PickFolderAsync(string title, string? startFolder);

    Task<string?> PickOpenFileAsync(string title, string typeName, IReadOnlyList<string> patterns);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string typeName, string extension);

    Task ShowInfoAsync(string title, string message);

    /// <summary>Affiche une image (QR code...) avec un texte d'explication.</summary>
    Task ShowImageAsync(string title, string message, byte[] png);

    Task<SaveChoice> AskSaveChangesAsync(string fileName);

    Task ShowErrorAsync(string message);
}
