using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Ligne de l'aperçu : une mission décalée.</summary>
public record DelayChangeRow(string Name, string OldTime, string NewTime, string Shift, bool IsDelayedMission);

/// <summary>Mission optionnelle qu'on peut désactiver pour absorber le retard.</summary>
public partial class CutChoiceViewModel(MissionViewModel mission, int gain, Action onChanged) : ViewModelBase
{
    public MissionViewModel Mission => mission;

    public string GainText => L.F("evite_x_de_retard", MissionTime.FormatDuration(gain));

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged();
}

/// <summary>
/// Gestion d'un retard : on indique de combien une mission est retardée, le logiciel calcule le décalage
/// en cascade et propose les missions optionnelles à désactiver ; l'orga valide, et peut annuler.
/// </summary>
public partial class DelayViewModel(MissionsViewModel missions) : ViewModelBase
{
    private List<(MissionViewModel Mission, int Start, bool Enabled)>? _undo;
    private bool _updatingCuts;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(OptimizeCommand))]
    private MissionViewModel? _mission;

    [ObservableProperty]
    private decimal? _delayMinutes = 15;

    [ObservableProperty]
    private IReadOnlyList<DelayChangeRow> _changes = [];

    [ObservableProperty]
    private IReadOnlyList<CutChoiceViewModel> _cuts = [];

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private string _lastApplied = "";

    public string Title => Mission is null ? "" : L.F("retard_sur_x_x", Mission.Name, Mission.StartText);

    /// <summary>Ouvre le panneau pour une mission, avec un retard proposé (ex. retard estimé en suivi).</summary>
    public void Open(MissionViewModel mission, int delayMinutes)
    {
        Mission = mission;
        DelayMinutes = Math.Max(5, delayMinutes);
        IsOpen = true;
        Recompute(resetCuts: true);
    }

    partial void OnMissionChanged(MissionViewModel? value) => OnPropertyChanged(nameof(Title));

    partial void OnDelayMinutesChanged(decimal? value)
    {
        if (IsOpen)
            Recompute(resetCuts: true);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Coche les missions optionnelles qui réduisent le plus le retard des missions essentielles.</summary>
    [RelayCommand(CanExecute = nameof(HasMission))]
    private void Optimize()
    {
        var plan = DelayPlanner.Optimize(Models(), Mission!.Model.Id, Delay, missions.OperationEndMinutes);
        _updatingCuts = true;
        foreach (var cut in Cuts)
            cut.IsSelected = plan.DisabledMissionIds.Contains(cut.Mission.Model.Id);
        _updatingCuts = false;
        Recompute(resetCuts: false);
    }

    [RelayCommand(CanExecute = nameof(HasMission))]
    private void Apply()
    {
        var plan = CurrentPlan();
        var byId = missions.Missions.ToDictionary(m => m.Model.Id);
        _undo = missions.Missions.Select(m => (m, m.StartMinutes, m.IsEnabled)).ToList();

        foreach (var id in plan.DisabledMissionIds)
            byId[id].IsEnabled = false;
        foreach (var change in plan.Changes)
            byId[change.MissionId].StartMinutes = change.NewStart;

        LastApplied = L.F("retard_de_x_applique_x_mission_s_decalee_s", MissionTime.FormatDuration(Delay), plan.Changes.Count)
                      + (plan.DisabledMissionIds.Count > 0 ? L.F("x_desactivee_s", plan.DisabledMissionIds.Count) : "");
        IsOpen = false;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        foreach (var (mission, start, enabled) in _undo!)
        {
            mission.StartMinutes = start;
            mission.IsEnabled = enabled;
        }

        _undo = null;
        LastApplied = "";
    }

    private bool HasMission => Mission is not null;

    private bool CanUndo => _undo is not null;

    private int Delay => Math.Max(0, (int)(DelayMinutes ?? 0));

    private List<AirsoftPlanner.Core.Domain.Mission> Models() => missions.Missions.Select(m => m.Model).ToList();

    private DelayPlan CurrentPlan() => DelayPlanner.Plan(Models(), Mission!.Model.Id, Delay, missions.OperationEndMinutes,
        Cuts.Where(c => c.IsSelected).Select(c => c.Mission.Model.Id).ToHashSet());

    private void Recompute(bool resetCuts)
    {
        if (_updatingCuts || Mission is null)
            return;

        if (!Mission.IsEnabled)
        {
            Summary = L.T("la_mission_est_desactivee_reactivez_la_pour_repe");
            Changes = [];
            Cuts = [];
            return;
        }

        var models = Models();
        if (resetCuts)
        {
            var byId = missions.Missions.ToDictionary(m => m.Model.Id);
            Cuts = DelayPlanner.SuggestCuts(models, Mission.Model.Id, Delay, missions.OperationEndMinutes)
                .Select(c => new CutChoiceViewModel(byId[c.Mission.Id], c.GainMinutes, () => Recompute(resetCuts: false)))
                .ToList();
        }

        var plan = CurrentPlan();
        var names = missions.Missions.ToDictionary(m => m.Model.Id, m => m.Name);
        Changes = plan.Changes
            .Select(c => new DelayChangeRow(names[c.MissionId], MissionTime.Format(c.OldStart), MissionTime.Format(c.NewStart),
                $"+{MissionTime.FormatDuration(c.Shift)}", c.MissionId == Mission.Model.Id))
            .ToList();

        Summary = plan.Changes.Count <= 1 && plan.OverflowMinutes == 0
            ? L.T("le_retard_est_absorbe_aucune_autre_mission_n_est")
            : string.Join(" · ", new[]
            {
                L.F("x_mission_s_decalee_s", plan.Changes.Count),
                plan.EssentialDelayMinutes > 0 ? L.F("missions_essentielles_retardees_jusqu_a_x", MissionTime.FormatDuration(plan.MaxEssentialDelayMinutes)) : L.T("aucune_mission_essentielle_retardee"),
                plan.OverflowMinutes > 0 ? L.F("depasse_la_fin_de_l_op_de_x", MissionTime.FormatDuration(plan.OverflowMinutes)) : null,
            }.OfType<string>());
    }
}
