using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Updates;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>
/// Mises à jour : recherche d'une nouvelle version sur GitHub (au démarrage, une fois par jour, ou à la demande),
/// puis téléchargement de l'archive et installation automatique (le logiciel redémarre).
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
        ? $"🔔 {release.Name} est disponible (version installée : {CurrentVersion.ToString(3)})."
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
        Status = "Recherche d'une mise à jour…";
        var release = await UpdateChecker.GetLatestAsync(Http);
        Status = "";
        if (release is null)
        {
            await dialogs.ShowInfoAsync("Mises à jour",
                $"Impossible de connaître la dernière version (pas de connexion Internet, ou dépôt pas encore public).\n\n{UpdateChecker.ProjectUrl}");
            return;
        }

        if (!UpdateChecker.IsNewer(release, CurrentVersion))
        {
            await dialogs.ShowInfoAsync("Mises à jour", $"Airsoft Planner est à jour (version {CurrentVersion.ToString(3)}).");
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

    /// <summary>Télécharge l'archive de la nouvelle version et lance son installation (le logiciel se ferme puis redémarre).</summary>
    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Available is not { } release)
            return;
        if (release.ArchiveUrl is null)
        {
            // Pas d'archive jointe à la version : page de téléchargement.
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
            var zip = Path.Combine(folder, "archive.zip");

            Status = $"Téléchargement de {release.Name}…";
            using (var response = await Http.GetAsync(release.ArchiveUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(zip);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total is > 0)
                        Status = $"Téléchargement de {release.Name}… {read * 100 / total:0} %";
                }
            }

            Status = "Préparation de l'installation…";
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, folder));
            var installer = Directory.GetFiles(folder, "Installer.ps1", SearchOption.AllDirectories).FirstOrDefault()
                            ?? throw new InvalidOperationException("Archive sans programme d'installation.");

            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{installer}\" -Relancer")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            Status = "";
            await dialogs.ShowErrorAsync($"Mise à jour impossible : {ex.Message}\nVous pouvez la télécharger depuis {release.PageUrl}");
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
