using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Updates;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>
/// Mises à jour : recherche d'une nouvelle version sur GitHub (au démarrage, une fois par jour, ou à la demande),
/// puis téléchargement du programme d'installation et installation automatique (le logiciel redémarre).
/// </summary>
public partial class UpdatesViewModel(IFileDialogService dialogs, Func<Task<bool>> confirmDiscardOrSave) : ViewModelBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static Version CurrentVersion => typeof(UpdatesViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(Banner))]
    private ReleaseInfo? _available;

    public bool HasUpdate => Available is not null;

    public string Banner => Available is { } release
        ? L.F("x_est_disponible_version_installee_x", release.Name, CurrentVersion.ToString(3))
        : "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "";

    /// <summary>Recherche discrète au démarrage (au plus une fois par jour).</summary>
    public async Task CheckSilentlyAsync()
    {
        var settings = AppSettings.Current;
        if (DateTimeOffset.Now - settings.LastUpdateCheck < TimeSpan.FromHours(20))
            return;
        settings.LastUpdateCheck = DateTimeOffset.Now;
        settings.Save();
        var release = await UpdateChecker.GetLatestAsync(Http);
        if (release is not null && UpdateChecker.IsNewer(release, CurrentVersion) && release.Tag != settings.SkippedUpdate)
            Available = release;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        Status = L.T("recherche_d_une_mise_a_jour");
        var release = await UpdateChecker.GetLatestAsync(Http);
        Status = "";
        if (release is null)
        {
            await dialogs.ShowInfoAsync(L.T("mises_a_jour"),
                L.F("impossible_de_connaitre_la_derniere_version_pas", UpdateChecker.ProjectUrl));
            return;
        }

        if (!UpdateChecker.IsNewer(release, CurrentVersion))
        {
            await dialogs.ShowInfoAsync(L.T("mises_a_jour"), L.F("airsoft_planner_est_a_jour_version_x", CurrentVersion.ToString(3)));
            return;
        }

        Available = release;
    }

    [RelayCommand]
    private void ShowNotes()
    {
        if (Available is { } release)
            Open(release.PageUrl);
    }

    /// <summary>Ne plus signaler cette version (la suivante le sera).</summary>
    [RelayCommand]
    private void Later()
    {
        if (Available is { } release)
        {
            AppSettings.Current.SkippedUpdate = release.Tag;
            AppSettings.Current.Save();
        }

        Available = null;
    }

    /// <summary>Télécharge le programme d'installation de la nouvelle version et le lance (le logiciel se ferme puis redémarre).</summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Available is not { } release)
            return;
        if (release.SetupUrl is null)
        {
            // Pas de programme d'installation joint à la version : page de téléchargement.
            Open(release.PageUrl);
            return;
        }

        if (!await confirmDiscardOrSave())
            return;

        IsBusy = true;
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "AirsoftPlanner-mise-a-jour", release.Tag);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            var setup = Path.Combine(folder, "AirsoftPlanner-Setup.exe");

            Status = L.F("telechargement_de_x", release.Name);
            using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(setup);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total is > 0)
                        Status = L.F("telechargement_de_x_x", release.Name, read * 100 / total);
                }
            }

            // Installation silencieuse au même endroit ; le programme d'installation relance le logiciel ensuite.
            Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELANCER=1")
            {
                UseShellExecute = true,
            });
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            Status = "";
            await dialogs.ShowErrorAsync(L.F("mise_a_jour_impossible_x_vous_pouvez_la_telechar", ex.Message, release.PageUrl));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstall => !IsBusy;

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Pas de navigateur par défaut.
        }
    }
}
