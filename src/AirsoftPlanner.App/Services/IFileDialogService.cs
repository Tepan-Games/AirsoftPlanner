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

    Task<SaveChoice> AskSaveChangesAsync(string fileName);

    Task ShowErrorAsync(string message);
}
