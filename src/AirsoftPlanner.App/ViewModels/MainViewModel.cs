using System;
using System.IO;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

public partial class MainViewModel(IFileDialogService dialogs) : ViewModelBase, IDisposable
{
    private OperationFile? _file;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private OperationViewModel? _operation;

    public bool HasOperation => Operation is not null;

    public string WindowTitle => _file is null
        ? "Airsoft Planner"
        : $"{Path.GetFileName(_file.Path)} — Airsoft Planner";

    [RelayCommand]
    private async Task NewOperationAsync()
    {
        var path = await dialogs.PickNewOperationFileAsync("Nouvelle OP");
        if (path is null)
            return;

        await RunAsync(() => Load(OperationFile.Create(path, Path.GetFileNameWithoutExtension(path), Environment.UserName)));
    }

    [RelayCommand]
    private async Task OpenOperationAsync()
    {
        var path = await dialogs.PickExistingOperationFileAsync();
        if (path is null)
            return;

        await RunAsync(() => Load(OperationFile.Open(path)));
    }

    [RelayCommand(CanExecute = nameof(HasOperation))]
    private async Task SaveAsync() => await RunAsync(() => _file!.Save());

    public void Dispose() => _file?.Dispose();

    private void Load(OperationFile file)
    {
        _file?.Dispose();
        _file = file;
        Operation = new OperationViewModel(file.Operation);
    }

    private async Task RunAsync(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await dialogs.ShowErrorAsync(ex.Message);
        }
    }
}
