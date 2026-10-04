using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AirsoftPlanner.App.Services;
using AirsoftPlanner.Core.Retex;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Ligne du tableau de bilan.</summary>
public record RetexTeamRow(TeamRetex Sheet, string Team, string Faction, string Missions, int Completed, string AverageDelay,
    string Distance, int PlayersOut, int Messages);

/// <summary>
/// RETEX : bilan de l'OP à partir de ce qui a été enregistré (positions, messages, objets, effectifs),
/// avec la chronologie et les PDF (global et par équipe, messages transmis compris).
/// </summary>
public partial class RetexViewModel : ViewModelBase
{
    private readonly Data.OperationFile _file;
    private readonly OperationViewModel _operation;
    private readonly TeamsViewModel _teams;
    private readonly FactionsViewModel _factions;
    private readonly TrackingViewModel _tracking;
    private readonly GameItemsViewModel _items;
    private readonly IFileDialogService _dialogs;
    private OperationRetex? _retex;

    public RetexViewModel(Data.OperationFile file, OperationViewModel operation, TeamsViewModel teams, FactionsViewModel factions,
        TrackingViewModel tracking, GameItemsViewModel items, IFileDialogService dialogs)
    {
        (_file, _operation, _teams, _factions, _tracking, _items, _dialogs) = (file, operation, teams, factions, tracking, items, dialogs);
    }

    public ObservableCollection<RetexTeamRow> Teams { get; } = [];

    public ObservableCollection<RetexEvent> Timeline { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TeamPdfCommand))]
    private RetexTeamRow? _selectedTeam;

    partial void OnSelectedTeamChanged(RetexTeamRow? value) => RefreshTimeline();

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Recalcule le bilan (à l'affichage de l'onglet, ou à la demande pendant l'OP).</summary>
    [RelayCommand]
    public void Refresh()
    {
        var dispatch = _tracking.Dispatch;
        _retex = RetexBuilder.Build(
            _tracking.Missions.Columns.Select(t => t.Model).ToList(), // équipes en jeu (inscriptions confirmées)
            _tracking.Missions.Missions.Select(m => m.Model).ToList(),
            _tracking.RecordedPositions,
            dispatch?.AllMessages ?? [],
            _tracking.RecordedItemEvents,
            _tracking.RecordedPlayerEvents,
            minutes => new DateTimeOffset(_operation.ToDateTime(minutes)),
            id => _items.Items.FirstOrDefault(i => i.Model.Id == id)?.Name ?? L.T("objet_supprime"),
            kind => ItemEventOption.Of(kind).Label,
            reason => OutReasonOption.Of(reason).Label);

        var selected = SelectedTeam?.Sheet.Team;
        Teams.Clear();
        foreach (var sheet in _retex.Teams)
            Teams.Add(new RetexTeamRow(sheet, sheet.Team.Name,
                _factions.Items.FirstOrDefault(f => f.Model.Id == sheet.Team.FactionId)?.Name ?? "",
                $"{sheet.MissionsPublished}/{sheet.MissionsPlanned}", sheet.MissionsCompleted, RetexGenerator.AverageDelay(sheet),
                $"{sheet.DistanceKm:0.0} km", sheet.PlayersOut, sheet.Messages.Count));
        SelectedTeam = Teams.FirstOrDefault(t => t.Sheet.Team == selected);
        RefreshTimeline();

        var ended = DateTimeOffset.Now > _file.Operation.EndsAt;
        Status = ended
            ? L.F("op_terminee_le_x", _file.Operation.EndsAt.LocalDateTime)
            : L.T("op_pas_encore_terminee_bilan_provisoire");
        Summary = $"{_retex.Teams.Sum(t => t.MissionsPublished)}/{_retex.Teams.Sum(t => t.MissionsPlanned)} missions diffusées · "
                  + L.F("x_terminees_x_km_parcourus", _retex.Teams.Sum(t => t.MissionsCompleted), _retex.Teams.Sum(t => t.DistanceKm))
                  + L.F("x_evenements", _retex.Timeline.Count);
        GlobalPdfCommand.NotifyCanExecuteChanged();
        AllTeamsPdfCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasRetex))]
    private async Task GlobalPdfAsync() => await GenerateAsync(folder =>
    {
        var path = Path.Combine(folder, PackageGenerator.SafeFileName(L.F("retex_x_pdf", _file.Operation.Name)));
        RetexGenerator.WriteGlobal(_retex!, Context(), path);
        return path;
    });

    [RelayCommand(CanExecute = nameof(HasSelectedTeam))]
    private async Task TeamPdfAsync() => await GenerateAsync(folder => WriteTeam(folder, SelectedTeam!.Sheet));

    [RelayCommand(CanExecute = nameof(HasRetex))]
    private async Task AllTeamsPdfAsync() => await GenerateAsync(folder =>
    {
        foreach (var sheet in _retex!.Teams)
            WriteTeam(folder, sheet);
        return folder;
    });

    private bool HasRetex => _retex is not null;

    private bool HasSelectedTeam => SelectedTeam is not null;

    private string WriteTeam(string folder, TeamRetex sheet)
    {
        var path = Path.Combine(folder, PackageGenerator.SafeFileName(L.F("retex_x_x_pdf", _file.Operation.Name, sheet.Team.Name)));
        RetexGenerator.WriteTeam(sheet, Context(), path);
        return path;
    }

    private RetexContext Context() => new(_file.Operation,
        (_tracking.Terrain.SelectedLayer ?? _tracking.Terrain.Layers.FirstOrDefault())?.Model,
        _factions.Items.ToDictionary(f => f.Model.Id, f => f.Color),
        _factions.Items.ToDictionary(f => f.Model.Id, f => f.Name));

    private async Task GenerateAsync(Func<string, string> write)
    {
        var folder = await _dialogs.PickFolderAsync(L.T("dossier_ou_enregistrer_le_retex"), null);
        if (folder is null)
            return;
        try
        {
            var path = await Task.Run(() => write(folder));
            Status = L.F("retex_enregistre_x", path);
            if (!OperatingSystem.IsWindows() || AppSettings.SuppressOpening)
                return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Action lancée par l'utilisateur : toute erreur (disque, PDF, image) est signalée sans fermer le logiciel.
            await _dialogs.ShowErrorAsync(L.F("generation_du_retex_impossible_x", ex.Message));
        }
    }

    private void RefreshTimeline()
    {
        Timeline.Clear();
        var events = SelectedTeam is { } row ? row.Sheet.Events : _retex?.Timeline ?? [];
        foreach (var e in events)
            Timeline.Add(e);
    }
}
