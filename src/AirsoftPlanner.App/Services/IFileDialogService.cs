using System.Threading.Tasks;

namespace AirsoftPlanner.App.Services;

public interface IFileDialogService
{
    Task<string?> PickNewOperationFileAsync(string suggestedName);

    Task<string?> PickExistingOperationFileAsync();

    Task ShowErrorAsync(string message);
}
