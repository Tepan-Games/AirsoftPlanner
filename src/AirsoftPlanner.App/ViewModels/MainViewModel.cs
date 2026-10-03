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
    private WorkspaceViewModel? _workspace;

    public bool HasOperation => Workspace is not null;

    public bool IsNightMode => AppSettings.Current.Theme == AppTheme.Night;

    public string ThemeButtonText => IsNightMode ? "☀ Mode jour" : "🌙 Mode nuit";

    /// <summary>Bascule jour / nuit, mémorisée pour les prochains lancements.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        AppSettings.Current.Theme = IsNightMode ? AppTheme.Day : AppTheme.Night;
        AppSettings.Current.Save();
        AppSettings.ApplyTheme();
        OnPropertyChanged(nameof(IsNightMode));
        OnPropertyChanged(nameof(ThemeButtonText));
    }

    public string WindowTitle => _file is null
        ? "Airsoft Planner"
        : $"{Path.GetFileName(_file.Path)} — Airsoft Planner";

    [RelayCommand]
    private async Task NewOperationAsync()
    {
        if (!await ConfirmDiscardOrSaveAsync())
            return;

        var path = await dialogs.PickNewOperationFileAsync("Nouvelle OP");
        if (path is null)
            return;

        await RunAsync(() => Load(OperationFile.Create(path, Path.GetFileNameWithoutExtension(path), Environment.UserName)));
    }

    [RelayCommand]
    private async Task OpenOperationAsync()
    {
        if (!await ConfirmDiscardOrSaveAsync())
            return;

        var path = await dialogs.PickExistingOperationFileAsync();
        if (path is null)
            return;

        await RunAsync(() => Load(OperationFile.Open(path)));
    }

    /// <summary>Ouvre directement un fichier d'OP (passé en argument au lancement, par exemple).</summary>
    public Task OpenFileAsync(string path) => RunAsync(() => Load(OperationFile.Open(path)));

    [RelayCommand(CanExecute = nameof(HasOperation))]
    private async Task SaveAsync() => await RunAsync(() => _file!.Save());

    /// <summary>
    /// Propose d'enregistrer les modifications en cours.
    /// Renvoie false si l'utilisateur annule (l'action demandée ne doit pas avoir lieu).
    /// </summary>
    public async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (_file is null || !_file.HasUnsavedChanges)
            return true;

        switch (await dialogs.AskSaveChangesAsync(Path.GetFileName(_file.Path)))
        {
            case SaveChoice.Save:
                var saved = false;
                await RunAsync(() =>
                {
                    _file.Save();
                    saved = true;
                });
                return saved;
            case SaveChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    public void Dispose() => _file?.Dispose();

    private void Load(OperationFile file)
    {
        _file?.Dispose();
        _file = file;
        Workspace = new WorkspaceViewModel(file, dialogs);
    }

    private async Task RunAsync(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            await dialogs.ShowErrorAsync(ex.Message);
        }
    }
}
