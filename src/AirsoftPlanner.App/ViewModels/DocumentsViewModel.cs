using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Documents;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public class RuleDocumentViewModel(RuleDocument rule) : ViewModelBase
{
    public RuleDocument Model => rule;

    public string Title
    {
        get => rule.Title;
        set => SetProperty(rule.Title, value, rule, (r, v) => r.Title = v);
    }

    public string Text
    {
        get => rule.Text;
        set => SetProperty(rule.Text, value, rule, (r, v) => r.Text = v);
    }

    public bool IsImported => rule.IsImported;

    public bool IsWritten => !rule.IsImported;

    public string KindText => rule.Origin == AcpRules.Origin
        ? L.F("reglement_officiel_telecharge_depuis_x_x_ko", "acp-rules.org", rule.FileContent.Length / 1024.0)
        : rule.IsImported
            ? L.F("fichier_importe_x_x_ko", rule.FileName, rule.FileContent.Length / 1024.0)
            : L.T("redige_dans_le_logiciel");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindText));
    }

    public void Replace(string fileName, byte[] content)
    {
        rule.FileName = fileName;
        rule.FileContent = content;
        OnPropertyChanged(nameof(KindText));
    }
}

/// <summary>Ligne du tableau de diffusion : le package d'une équipe et son état.</summary>
public partial class PackageRowViewModel(TeamViewModel team) : ViewModelBase
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    public TeamViewModel Team => team;

    public TeamPackage? Package { get; set; }

    [ObservableProperty]
    private PackageStatus _status;

    [ObservableProperty]
    private string _receivedByInput = "";

    public string ContactText => team.Leader is { } leader
        ? string.Join(" · ", new[] { leader.DisplayName, leader.Phone, leader.Email }.Where(s => s.Length > 0))
        : L.T("pas_de_chef_d_equipe_onglet_equipes");

    public string StatusLabel => Status switch
    {
        PackageStatus.ReadyToSend => L.T("pret_a_envoyer"),
        PackageStatus.AwaitingConfirmation => L.T("envoye_en_attente_de_confirmation"),
        PackageStatus.Received => L.T("recu"),
        PackageStatus.Outdated => Package?.SentAt is null ? L.T("contenu_modifie_a_regenerer") : L.T("contenu_modifie_a_renvoyer"),
        _ => L.T("pas_encore_genere"),
    };

    public string StatusColor => Status switch
    {
        PackageStatus.ReadyToSend => "#1565C0",
        PackageStatus.AwaitingConfirmation => "#EF6C00",
        PackageStatus.Received => "#2E7D32",
        PackageStatus.Outdated => "#C62828",
        _ => "#757575",
    };

    public string HistoryText => Package is null || Package.GeneratedAt is null
        ? ""
        : string.Join(" · ", new[]
        {
            L.F("genere_le_x", Format(Package.GeneratedAt)),
            Package.SentAt is null ? null : L.F("envoye_le_x", Format(Package.SentAt)),
            Package.ReceivedAt is null ? null : L.F("recu_le_x_x", Format(Package.ReceivedAt), (Package.ReceivedBy.Length > 0 ? $" par {Package.ReceivedBy}" : "")),
        }.OfType<string>());

    public void Refresh(PackageStatus status)
    {
        Status = status;
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(HistoryText));
        OnPropertyChanged(nameof(ContactText));
        if (ReceivedByInput.Length == 0 && team.Leader is { } leader)
            ReceivedByInput = leader.DisplayName;
    }

    private static string Format(DateTimeOffset? date) => date!.Value.LocalDateTime.ToString("d MMM HH:mm", French);
}

/// <summary>Règles du jeu et diffusion des packages (règles + ordres de mission initiaux) aux équipes.</summary>
public partial class DocumentsViewModel : ViewModelBase
{
    private readonly OperationFile _file;
    private readonly IFileDialogService _dialogs;
    private readonly TeamsViewModel _teams;
    private readonly FactionsViewModel _factions;
    private readonly TerrainViewModel _terrain;
    private readonly MissionsViewModel _missions;
    private readonly GameItemsViewModel _items;
    private readonly List<TeamPackage> _packages;

    public DocumentsViewModel(OperationFile file, IFileDialogService dialogs, TeamsViewModel teams, FactionsViewModel factions,
        TerrainViewModel terrain, MissionsViewModel missions, GameItemsViewModel items)
    {
        _file = file;
        _dialogs = dialogs;
        _teams = teams;
        _factions = factions;
        _terrain = terrain;
        _missions = missions;
        _items = items;
        _packages = file.LoadTeamPackages().ToList();
        Rules = new ObservableCollection<RuleDocumentViewModel>(file.LoadRuleDocuments().Select(r => new RuleDocumentViewModel(r)));
        SelectedRule = Rules.FirstOrDefault();
        teams.Items.CollectionChanged += (_, _) => Refresh();
        missions.ScheduleChanged += Refresh;
        Refresh();
    }

    public ObservableCollection<RuleDocumentViewModel> Rules { get; }

    public ObservableCollection<PackageRowViewModel> Packages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRule))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRuleCommand), nameof(OpenRuleCommand), nameof(MoveRuleUpCommand),
        nameof(MoveRuleDownCommand), nameof(ReplaceRuleFileCommand))]
    private RuleDocumentViewModel? _selectedRule;

    public bool HasSelectedRule => SelectedRule is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateSelectedCommand), nameof(MarkSentCommand), nameof(MarkReceivedCommand),
        nameof(OpenPackageFolderCommand))]
    private PackageRowViewModel? _selectedPackage;

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private bool _isGenerating;

    // ----- Règles -----

    /// <summary>Règlement ACP choisi pour l'OP (informations générales).</summary>
    public bool IsAcp => _file.Operation.RuleSet == GameRuleSet.Acp;

    [ObservableProperty]
    private string _acpStatus = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAcpRulesCommand))]
    private bool _isDownloadingAcp;

    /// <summary>
    /// Règlement choisi dans les informations générales : ACP → PDF officiel ajouté aux documents (téléchargé s'il
    /// n'y est pas encore) ; règles de l'OP → document ACP retiré.
    /// </summary>
    public async Task ApplyRuleSetAsync(GameRuleSet set)
    {
        OnPropertyChanged(nameof(IsAcp));
        if (set == GameRuleSet.Acp)
        {
            if (!Rules.Any(r => r.Model.Origin == AcpRules.Origin))
                await UpdateAcpRulesAsync();
            return;
        }

        foreach (var rule in Rules.Where(r => r.Model.Origin == AcpRules.Origin).ToList())
        {
            _file.Remove(rule.Model);
            Rules.Remove(rule);
        }
        SelectedRule = Rules.FirstOrDefault();
        AcpStatus = "";
        Refresh();
    }

    /// <summary>Télécharge la dernière version du règlement ACP (ajoutée en tête des documents, ou remplaçant l'ancienne).</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAcpRules))]
    private async Task UpdateAcpRulesAsync()
    {
        IsDownloadingAcp = true;
        AcpStatus = L.T("telechargement_du_reglement_acp");
        try
        {
            var file = await AcpRulesDownloader.DownloadAsync();
            var existing = Rules.FirstOrDefault(r => r.Model.Origin == AcpRules.Origin);
            if (existing is not null && existing.Model.FileContent.AsSpan().SequenceEqual(file.Content))
            {
                AcpStatus = L.F("reglement_acp_x_deja_a_jour", file.Version);
                return;
            }

            if (existing is not null)
            {
                existing.Model.Title = AcpRules.Title(file.Version);
                existing.Replace(AcpRules.FileName(file.Version), file.Content);
                existing.Refresh();
                SelectedRule = existing;
            }
            else
            {
                // En tête des documents : c'est le règlement de référence de l'OP.
                foreach (var rule in Rules)
                    rule.Model.SortOrder++;
                var acp = new RuleDocument
                {
                    Title = AcpRules.Title(file.Version),
                    FileName = AcpRules.FileName(file.Version),
                    FileContent = file.Content,
                    Origin = AcpRules.Origin,
                    SortOrder = 0,
                };
                _file.Add(acp);
                var viewModel = new RuleDocumentViewModel(acp);
                Rules.Insert(0, viewModel);
                SelectedRule = viewModel;
            }

            AcpStatus = L.F("reglement_acp_x_ajoute_aux_documents", file.Version);
            Refresh();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            AcpStatus = L.F("reglement_acp_non_telecharge_x", ex.Message);
            await _dialogs.ShowErrorAsync(AcpStatus);
        }
        finally
        {
            IsDownloadingAcp = false;
        }
    }

    private bool CanUpdateAcpRules => !IsDownloadingAcp;

    [RelayCommand]
    private async Task ImportRuleAsync()
    {
        var path = await _dialogs.PickDocumentFileAsync();
        if (path is null)
            return;

        AddRule(new RuleDocument
        {
            Title = Path.GetFileNameWithoutExtension(path),
            FileName = Path.GetFileName(path),
            FileContent = await File.ReadAllBytesAsync(path),
        });
    }

    [RelayCommand]
    private void WriteRule() => AddRule(new RuleDocument
    {
        Title = L.T("regles_specifiques_de_l_op"),
        Text = """
               # Sécurité
               - Lunettes ou masque homologués obligatoires en zone de jeu.
               - Arrêt immédiat du jeu au cri « STOP JEU ».

               # Puissances des répliques
               - Fusils d'assaut : 1,2 J maximum, tir en rafale autorisé.
               - Répliques de précision : 2,3 J maximum, distance d'engagement minimale 30 m.

               # Touches et respawn
               - Une touche n'importe où (réplique comprise) : annoncer « touché », chiffon rouge.
               - Retour au respawn de la faction, 10 minutes d'attente.
               """,
    });

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void RemoveRule()
    {
        var rule = SelectedRule!;
        _file.Remove(rule.Model);
        Rules.Remove(rule);
        SelectedRule = Rules.FirstOrDefault();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void MoveRuleUp() => MoveRule(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void MoveRuleDown() => MoveRule(1);

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private async Task ReplaceRuleFileAsync()
    {
        var path = await _dialogs.PickDocumentFileAsync();
        if (path is null)
            return;

        SelectedRule!.Replace(Path.GetFileName(path), await File.ReadAllBytesAsync(path));
        OnPropertyChanged(nameof(SelectedRule));
        Refresh();
    }

    /// <summary>Ouvre le document avec le logiciel associé (lecteur PDF, Word...).</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private async Task OpenRuleAsync()
    {
        var rule = SelectedRule!.Model;
        try
        {
            var folder = Directory.CreateTempSubdirectory("airsoft-planner-").FullName;
            string path;
            if (rule.IsImported)
            {
                path = Path.Combine(folder, PackageGenerator.SafeFileName(rule.FileName));
                await File.WriteAllBytesAsync(path, rule.FileContent);
            }
            else
            {
                path = Path.Combine(folder, PackageGenerator.SafeFileName(rule.Title + ".pdf"));
                PackageGenerator.WriteRulesPdf(_file.Operation, rule, path);
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            await _dialogs.ShowErrorAsync(L.F("impossible_d_ouvrir_le_document_x", ex.Message));
        }
    }

    // ----- Packages -----

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAllAsync() => await GenerateAsync(Packages.ToList());

    [RelayCommand(CanExecute = nameof(CanGenerateSelected))]
    private async Task GenerateSelectedAsync() => await GenerateAsync([SelectedPackage!]);

    [RelayCommand(CanExecute = nameof(HasSelectedPackage))]
    private void MarkSent()
    {
        var package = SelectedPackage!.Package;
        if (package is null || package.GeneratedAt is null)
            return;

        package.SentAt = DateTimeOffset.Now;
        package.ReceivedAt = null;
        package.ReceivedBy = "";
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPackage))]
    private void MarkReceived()
    {
        var row = SelectedPackage!;
        if (row.Package is not { GeneratedAt: not null } package)
            return;

        package.SentAt ??= DateTimeOffset.Now;
        package.ReceivedAt = DateTimeOffset.Now;
        package.ReceivedBy = row.ReceivedByInput.Trim();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPackage))]
    private async Task OpenPackageFolderAsync()
    {
        if (SelectedPackage?.Package?.OutputPath is not { Length: > 0 } path || !Directory.Exists(path))
        {
            await _dialogs.ShowErrorAsync(L.T("le_dossier_du_package_est_introuvable_generez_le"));
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Recalcule l'état de chaque package (à appeler quand l'onglet s'affiche ou après une modification).</summary>
    public void Refresh()
    {
        var selectedTeam = SelectedPackage?.Team;
        if (!Packages.Select(p => p.Team).SequenceEqual(_missions.Columns))
        {
            Packages.Clear();
            foreach (var team in _missions.Columns)
                Packages.Add(new PackageRowViewModel(team));
        }

        foreach (var row in Packages)
        {
            row.Package = _packages.FirstOrDefault(p => p.TeamId == row.Team.Model.Id);
            row.Refresh(PackageState.Of(row.Package, Fingerprint(row.Team)));
        }

        SelectedPackage = Packages.FirstOrDefault(p => p.Team == selectedTeam) ?? SelectedPackage;
        var counts = Packages.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count());
        Summary = Packages.Count == 0 ? L.T("aucune_equipe_2") : string.Join(" · ", new[]
        {
            $"{counts.GetValueOrDefault(PackageStatus.Received)}/{Packages.Count} reçus",
            counts.GetValueOrDefault(PackageStatus.AwaitingConfirmation) is > 0 and var waiting ? L.F("x_en_attente", waiting) : null,
            counts.GetValueOrDefault(PackageStatus.Outdated) is > 0 and var outdated ? L.F("x_a_renvoyer", outdated) : null,
            counts.GetValueOrDefault(PackageStatus.NotGenerated) is > 0 and var missing ? L.F("x_a_generer", missing) : null,
        }.OfType<string>());
        GenerateAllCommand.NotifyCanExecuteChanged();
    }

    private bool HasSelectedPackage => SelectedPackage is not null;

    private bool CanGenerate => !IsGenerating && Packages.Count > 0;

    private bool CanGenerateSelected => !IsGenerating && SelectedPackage is not null;

    partial void OnIsGeneratingChanged(bool value)
    {
        GenerateAllCommand.NotifyCanExecuteChanged();
        GenerateSelectedCommand.NotifyCanExecuteChanged();
    }

    private async Task GenerateAsync(IReadOnlyList<PackageRowViewModel> rows)
    {
        var lastFolder = _packages.Select(p => p.OutputPath).FirstOrDefault(p => p.Length > 0);
        var root = await _dialogs.PickFolderAsync(L.T("dossier_ou_creer_les_packages"), lastFolder is null ? null : Path.GetDirectoryName(lastFolder));
        if (root is null)
            return;

        IsGenerating = true;
        try
        {
            foreach (var row in rows)
            {
                var input = BuildInput(row.Team);
                var folder = await Task.Run(() => PackageGenerator.Generate(input, root));
                var package = _packages.FirstOrDefault(p => p.TeamId == row.Team.Model.Id);
                if (package is null)
                {
                    package = new TeamPackage { TeamId = row.Team.Model.Id };
                    _file.Add(package);
                    _packages.Add(package);
                }

                // Un nouveau contenu doit être renvoyé : l'envoi et la réception précédents ne valent plus.
                var changed = package.Fingerprint != Fingerprint(row.Team);
                package.GeneratedAt = DateTimeOffset.Now;
                package.Fingerprint = Fingerprint(row.Team);
                package.OutputPath = folder;
                if (changed)
                {
                    package.SentAt = null;
                    package.ReceivedAt = null;
                    package.ReceivedBy = "";
                }
            }
        }
        catch (Exception ex)
        {
            // Action lancée par l'utilisateur : toute erreur (disque, PDF, image) est signalée sans fermer le logiciel.
            await _dialogs.ShowErrorAsync(L.F("generation_impossible_x", ex.Message));
        }
        finally
        {
            IsGenerating = false;
            Refresh();
        }
    }

    private PackageInput BuildInput(TeamViewModel team)
    {
        var faction = team.Faction;
        var commandTeam = faction?.CommandTeam;
        var missions = _missions.Missions.Select(m => m.Model).ToList();

        // Enrôlement de l'application Android : code de l'équipe (créé au besoin) et adresse publiée du PC de l'OP.
        if (team.EnrollmentCode.Length == 0)
            team.EnrollmentCode = Core.Gps.EnrollmentCodes.Generate(_teams.Items.Select(t => t.EnrollmentCode).Where(c => c.Length > 0).ToList());
        var port = AppSettings.Current.GpsServerPort;
        var server = _file.Operation.ServerAddress.Length > 0
            ? Core.Gps.EnrollmentLink.ServerUrl(_file.Operation.ServerAddress, port)
            : Services.Gps.LocalGpsServer.LocalAddresses(port).FirstOrDefault();
        return new PackageInput(
            _file.Operation,
            team.Model,
            team.Members.Select(m => m.Model).ToList(),
            faction?.Model,
            commandTeam?.Model,
            commandTeam?.Leader?.Model,
            missions.Where(m => m.IsEnabled && m.TeamIds.Contains(team.Model.Id)).OrderBy(m => m.StartMinutes).ToList(),
            missions.ToDictionary(m => m.Id),
            _terrain.Zones.ToDictionary(z => z.Model.Id, z => z.Model),
            _items.Items.ToDictionary(i => i.Model.Id, i => i.Model),
            Rules.Select(r => r.Model).ToList(),
            (_terrain.SelectedLayer ?? _terrain.Layers.FirstOrDefault())?.Model,
            server,
            _factions.Items.ToDictionary(f => f.Model.Id, f => f.Color));
    }

    private string Fingerprint(TeamViewModel team) => PackageFingerprint.Compute(
        team.Model,
        team.Faction?.Model,
        _missions.Missions.Select(m => m.Model).Where(m => m.TeamIds.Contains(team.Model.Id)),
        _terrain.Zones.ToDictionary(z => z.Model.Id, z => z.Model),
        Rules.Select(r => r.Model));

    private void AddRule(RuleDocument rule)
    {
        rule.SortOrder = Rules.Count == 0 ? 0 : Rules.Max(r => r.Model.SortOrder) + 1;
        _file.Add(rule);
        var viewModel = new RuleDocumentViewModel(rule);
        Rules.Add(viewModel);
        SelectedRule = viewModel;
        Refresh();
    }

    private void MoveRule(int offset)
    {
        var rule = SelectedRule!;
        var index = Rules.IndexOf(rule);
        var target = index + offset;
        if (target < 0 || target >= Rules.Count)
            return;

        Rules.Move(index, target);
        for (var i = 0; i < Rules.Count; i++)
            Rules[i].Model.SortOrder = i;
        SelectedRule = rule;
        Refresh();
    }
}
