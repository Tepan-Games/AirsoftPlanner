using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Data;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    /// <summary>Intervalle de synchronisation automatique en travail partagé.</summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(2);

    private readonly IFileDialogService dialogs;
    private readonly DispatcherTimer _syncTimer;
    private OperationFile? _file;

    public MainViewModel(IFileDialogService dialogs)
    {
        this.dialogs = dialogs;
        _syncTimer = new DispatcherTimer(SyncInterval, DispatcherPriority.Background, async (_, _) => await SyncSilentlyAsync());
        Updates = new UpdatesViewModel(dialogs, ConfirmDiscardOrSaveAsync);
        if (!AppSettings.SuppressOpening)
            Dispatcher.UIThread.Post(async () => await Updates.CheckSilentlyAsync(), DispatcherPriority.Background);
    }

    /// <summary>Nouvelle version disponible sur GitHub, installation.</summary>
    public UpdatesViewModel Updates { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOperation), nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(MergeCopyCommand), nameof(ConfigureSharingCommand))]
    private WorkspaceViewModel? _workspace;

    public bool HasOperation => Workspace is not null;

    public bool IsNightMode => AppSettings.Current.Theme == AppTheme.Night;

    public System.Collections.Generic.IReadOnlyList<CoordinateFormatOption> CoordinateFormats => CoordinateFormatOption.All;

    /// <summary>Format d'affichage des coordonnées (paramètre de l'application, mémorisé).</summary>
    public CoordinateFormatOption DisplayCoordinateFormat
    {
        get => CoordinateFormatOption.Of(AppSettings.Current.CoordinateFormat);
        set
        {
            if (value is null)
                return;

            AppSettings.SetCoordinateFormat(value.Value);
            OnPropertyChanged();
        }
    }

    public string ThemeButtonText => IsNightMode ? L.T("mode_jour") : L.T("mode_nuit");

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
        ? L.T("airsoft_planner")
        : L.F("x_airsoft_planner", Path.GetFileName(_file.Path));

    [RelayCommand]
    private async Task NewOperationAsync()
    {
        if (!await ConfirmDiscardOrSaveAsync())
            return;

        var path = await dialogs.PickNewOperationFileAsync(L.T("nouvelle_op"));
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

        await OpenFileAsync(path);
    }

    /// <summary>Ouvre directement un fichier d'OP (passé en argument au lancement, par exemple).</summary>
    public async Task OpenFileAsync(string path)
    {
        await RunAsync(() => Load(OperationFile.Open(path)));
        if (SharedPath is not null)
            await SyncNowAsync();
    }

    [RelayCommand(CanExecute = nameof(HasOperation))]
    private async Task SaveAsync()
    {
        await RunAsync(() => _file!.Save());
        if (SharedPath is not null)
            await SyncNowAsync();
    }

    // ----- Fusion d'une copie -----

    /// <summary>
    /// Fusionne une autre copie de la même OP (modifiée par un autre orga) : ce qui n'existe que là-bas est ajouté,
    /// et pour un élément modifié des deux côtés, la version la plus récente est retenue.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasOperation))]
    private async Task MergeCopyAsync()
    {
        var path = await dialogs.PickExistingOperationFileAsync();
        if (path is null)
            return;

        MergeReport? report = null;
        await RunAsync(() =>
        {
            _file!.Save();
            report = OperationMerger.Merge(_file, path);
        });
        if (report is null)
            return;

        Reload();
        await dialogs.ShowInfoAsync(L.T("fusion_terminee"), report.Changes == 0
            ? L.T("aucune_difference_ce_fichier_contient_deja_toute")
            : L.F("x_element_s_ajoute_s_x_mis_a_jour_x_supprime_s", report.Added, report.Updated, report.Deleted)
              + (report.KeptLocal > 0 ? L.F("x_element_s_plus_recent_s_ici_conserve_s", report.KeptLocal) : ""));
    }

    // ----- Travail partagé (OneDrive, Google Drive, Dropbox, partage réseau) -----

    /// <summary>Fichier partagé associé à l'OP ouverte sur ce poste, ou null.</summary>
    public string? SharedPath => OperationId is { } id && AppSettings.Current.SharedFiles.TryGetValue(id, out var path) ? path : null;

    public bool IsShared => SharedPath is not null;

    [ObservableProperty]
    private string _sharingStatus = "";

    /// <summary>
    /// Active le travail partagé : on choisit l'emplacement du fichier partagé dans un dossier synchronisé
    /// (OneDrive...). Les autres orgas ouvrent une copie de ce fichier et activent le partage sur le même fichier.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasOperation))]
    private async Task ConfigureSharingAsync()
    {
        var path = await dialogs.PickSaveFileAsync(
            L.T("fichier_partage_dans_onedrive_google_drive_dropb"),
            Path.GetFileName(_file!.Path), L.T("fichier_d_op_partage"), OperationFile.Extension);
        if (path is null)
            return;

        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(_file.Path), StringComparison.OrdinalIgnoreCase))
        {
            await dialogs.ShowErrorAsync(L.T("le_fichier_partage_doit_etre_different_de_votre"));
            return;
        }

        AppSettings.Current.SharedFiles[OperationId!] = path;
        AppSettings.Current.Save();
        RefreshSharing();
        await SyncNowAsync();
    }

    [RelayCommand(CanExecute = nameof(IsShared))]
    private void StopSharing()
    {
        AppSettings.Current.SharedFiles.Remove(OperationId!);
        AppSettings.Current.Save();
        RefreshSharing();
    }

    [RelayCommand(CanExecute = nameof(IsShared))]
    private async Task SyncNowAsync()
    {
        if (_file is null || SharedPath is not { } shared)
            return;

        try
        {
            var report = SharedSync.Sync(_file, shared);
            if (report.Received.Changes > 0)
                Reload();
            SharingStatus = L.F("partage_synchronise_a_x", DateTime.Now)
                            + (report.Received.Changes > 0 ? L.F("x_modification_s_recue_s", report.Received.Changes) : "")
                            + (report.ConflictCopiesMerged > 0 ? L.F("x_copie_s_en_conflit_fusionnee_s", report.ConflictCopiesMerged) : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DbUpdateException)
        {
            // Dossier synchronisé momentanément indisponible (hors ligne...) : on réessaiera au prochain cycle.
            SharingStatus = L.F("partage_echec_de_synchronisation_a_x_x", DateTime.Now, ex.Message);
        }
    }

    private async Task SyncSilentlyAsync()
    {
        if (IsShared)
            await SyncNowAsync();
    }

    private string? OperationId => _file?.Context.Operations.IgnoreQueryFilters().AsNoTracking().Select(o => o.Id).SingleOrDefault().ToString();

    private void RefreshSharing()
    {
        OnPropertyChanged(nameof(SharedPath));
        OnPropertyChanged(nameof(IsShared));
        StopSharingCommand.NotifyCanExecuteChanged();
        SyncNowCommand.NotifyCanExecuteChanged();
        SharingStatus = IsShared ? L.F("partage_x", Path.GetFileName(SharedPath)) : "";
        if (IsShared)
            _syncTimer.Start();
        else
            _syncTimer.Stop();
    }

    // ----- Installation -----

    public string VersionText => L.F("airsoft_planner_x", typeof(MainViewModel).Assembly.GetName().Version?.ToString(3));

    /// <summary>Associe les fichiers .aop au logiciel (utilisateur courant) : ouverture par double-clic.</summary>
    [RelayCommand]
    private async Task AssociateFilesAsync()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            FileAssociation.Register();
            await dialogs.ShowInfoAsync(L.T("fichiers_aop"), L.T("les_fichiers_d_op_aop_s_ouvrent_desormais_dans_a"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            await dialogs.ShowErrorAsync(L.F("association_impossible_x", ex.Message));
        }
    }

    /// <summary>Dépôt du projet : code source, nouvelles versions, signalement de problèmes.</summary>
    public const string ProjectSite = "https://github.com/Tepan-Games/AirsoftPlanner";

    [RelayCommand]
    private async Task AboutAsync() => await dialogs.ShowInfoAsync(L.T("a_propos"),
        L.F("x_tepan_games_logiciel_de_preparation_et_de_suiv", VersionText)
        + L.F("code_source_et_nouvelles_versions_x", ProjectSite)
        + L.T("cartes_ign_geoplateforme_licence_ouverte_pdf_que"));

    /// <summary>Change la langue : enregistrée pour ce poste, appliquée en redémarrant le logiciel (l'OP ouverte est rouverte).</summary>
    [RelayCommand]
    private async Task ChangeLanguageAsync(string code)
    {
        if (code == AirsoftPlanner.Core.Localization.L.Code)
            return;
        AppSettings.Current.Language = code;
        AppSettings.Current.Save();
        if (!await ConfirmDiscardOrSaveAsync())
            return;
        try
        {
            var arguments = _file is null ? "" : $"\"{_file.Path}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false });
            (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Redémarrage impossible : la langue sera appliquée au prochain lancement.
        }
    }

    [RelayCommand]
    private void OpenProjectSite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ProjectSite) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Pas de navigateur par défaut : l'adresse reste indiquée dans « À propos ».
        }
    }

    // ----- Fichier -----

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
                if (saved && IsShared)
                    await SyncNowAsync();
                return saved;
            case SaveChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        _syncTimer.Stop();
        StopGps();
        _file?.Dispose();
    }

    private void StopGps()
    {
        // Exécuté hors du fil de l'interface pour éviter tout blocage pendant l'arrêt du serveur.
        if (Workspace?.Tracking.Gps is { } gps)
            Task.Run(() => gps.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
    }

    private void Load(OperationFile file)
    {
        StopGps();
        _file?.Dispose();
        _file = file;
        Workspace = new WorkspaceViewModel(file, dialogs);
        RefreshSharing();
    }

    /// <summary>
    /// Recharge l'OP après une fusion ou une synchronisation (nouveaux éléments à afficher), sans couper
    /// la réception GPS ni perdre l'heure suivie.
    /// </summary>
    private void Reload()
    {
        var old = Workspace;
        var path = _file!.Path;
        _file.Dispose();
        _file = OperationFile.Open(path);
        var workspace = new WorkspaceViewModel(_file, dialogs);
        if (old is not null)
        {
            workspace.Tracking.IsSimulation = old.Tracking.IsSimulation;
            workspace.Tracking.SimulatedMinutes = old.Tracking.SimulatedMinutes;
            workspace.Tracking.ShowTrails = old.Tracking.ShowTrails;
            if (old.Tracking.Gps is { } gps)
            {
                gps.Rebind(_file, workspace.Tracking, workspace.Teams, workspace.Vehicles);
                workspace.AttachGps(gps);
                workspace.Tracking.Gps = gps;
            }
        }

        Workspace = workspace;
    }

    private async Task RunAsync(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                       or DbUpdateException)
        {
            await dialogs.ShowErrorAsync(ex.Message);
        }
    }
}
