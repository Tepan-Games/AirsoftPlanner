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

    Task<SaveChoice> AskSaveChangesAsync(string fileName);

    Task ShowErrorAsync(string message);
}
