using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Résultat de mission proposé dans une liste.</summary>
public record MissionResultOption(MissionResult Value, string Label)
{
    public static IReadOnlyList<MissionResultOption> All { get; } =
        MissionResults.All.Select(r => new MissionResultOption(r, $"{MissionResults.Symbol(r)} {MissionResults.Label(r)}".Trim())).ToList();

    public static MissionResultOption Of(MissionResult result) => All.First(o => o.Value == result);

    public override string ToString() => Label;
}

/// <summary>Condition de mission proposée dans une liste.</summary>
public record MissionConditionOption(MissionCondition Value, string Label)
{
    public static IReadOnlyList<MissionConditionOption> All { get; } =
        MissionResults.Conditions.Select(c => new MissionConditionOption(c, MissionResults.ConditionLabel(c))).ToList();

    public static MissionConditionOption Of(MissionCondition condition) => All.First(o => o.Value == condition);

    public override string ToString() => Label;
}

public class MissionViewModel(Mission mission, MissionsViewModel owner) : ViewModelBase
{
    private const string DefaultColor = "#607D8B";
    private IReadOnlyList<string> _issues = [];

    public Mission Model => mission;

    public string Name
    {
        get => mission.Name;
        set => SetProperty(mission.Name, value, mission, (m, v) => m.Name = v);
    }

    public string Description
    {
        get => mission.Description;
        set => SetProperty(mission.Description, value, mission, (m, v) => m.Description = v);
    }

    public bool IsEssential
    {
        get => mission.IsEssential;
        set => SetScheduleProperty(mission.IsEssential, value, v => mission.IsEssential = v);
    }

    public bool IsEnabled
    {
        get => mission.IsEnabled;
        set => SetScheduleProperty(mission.IsEnabled, value, v => mission.IsEnabled = v);
    }

    public int StartMinutes
    {
        get => mission.StartMinutes;
        set
        {
            if (SetScheduleProperty(mission.StartMinutes, value, v => mission.StartMinutes = v))
                OnTimesChanged();
        }
    }

    public int EndMinutes => mission.EndMinutes;

    public decimal? DurationMinutes
    {
        get => mission.DurationMinutes;
        set
        {
            var minutes = Math.Max(5, (int)(value ?? 5));
            if (SetScheduleProperty(mission.DurationMinutes, minutes, v => mission.DurationMinutes = v))
                OnTimesChanged();
        }
    }

    public string StartText
    {
        get => MissionTime.Format(mission.StartMinutes);
        set
        {
            if (!MissionTime.TryParse(value, out var minutes))
                throw new FormatException(L.T("heure_non_reconnue_ex_10_30"));
            // Une heure plus tôt que le début de l'OP désigne le lendemain (OP de nuit).
            if (minutes < owner.OperationStartMinutes && minutes + MissionTime.MinutesPerDay <= owner.OperationEndMinutes)
                minutes += MissionTime.MinutesPerDay;
            StartMinutes = minutes;
        }
    }

    public string TimeRangeText =>
        $"{MissionTime.Format(mission.StartMinutes)} – {MissionTime.Format(mission.EndMinutes)} ({MissionTime.FormatDuration(mission.DurationMinutes)})";

    public ZoneViewModel? Zone
    {
        get => owner.Zones.FirstOrDefault(z => z.Model.Id == mission.ZoneId);
        set
        {
            if (value?.Model.Id == mission.ZoneId)
                return;

            mission.ZoneId = value?.Model.Id;
            RefreshZone();
        }
    }

    public string ZoneName => Zone?.Name ?? "";

    /// <summary>Points d'intérêt diffusés aux équipes seulement pendant cette mission.</summary>
    public string MissionPoints => string.Join(", ", owner.Zones
        .Where(z => z.Model.Visibility == ZoneVisibility.DuringMission && z.Model.VisibleMissionId == mission.Id)
        .Select(z => z.DisplayName));

    public bool HasMissionPoints => MissionPoints.Length > 0;

    /// <summary>Couleur de la mission sur la frise : celle de sa zone.</summary>
    public string Color => Zone?.Color ?? DefaultColor;

    public IReadOnlyList<Guid> TeamIds => mission.TeamIds;

    /// <summary>Équipes engagées, dans l'ordre des colonnes de la frise.</summary>
    public string TeamsText => string.Join(", ", owner.Columns.Where(t => mission.TeamIds.Contains(t.Model.Id)).Select(t => t.Name));

    public bool HasMaxPlayers
    {
        get => mission.MaxPlayers is not null;
        set
        {
            if (value == HasMaxPlayers)
                return;

            mission.MaxPlayers = value ? Math.Max(1, owner.AssignedPlayers(this)) : null;
            OnMaxPlayersChanged();
        }
    }

    public decimal? MaxPlayers
    {
        get => mission.MaxPlayers;
        set
        {
            if (!HasMaxPlayers || value is null || (int)value == mission.MaxPlayers)
                return;

            mission.MaxPlayers = Math.Max(1, (int)value);
            OnMaxPlayersChanged();
        }
    }

    public string PlayersText
    {
        get
        {
            var assigned = owner.AssignedPlayers(this);
            return mission.MaxPlayers is { } max ? L.F("x_joueur_s_affecte_s_sur_x_maximum", assigned, max) : L.F("x_joueur_s_affecte_s", assigned);
        }
    }

    /// <summary>Matériel de jeu utilisé par la mission.</summary>
    public IReadOnlyList<MissionItemUseViewModel> ItemUses => mission.Items
        .Select(use => owner.FindItem(use.ItemId) is { } item ? new MissionItemUseViewModel(item, use.Quantity, this) : null)
        .OfType<MissionItemUseViewModel>()
        .ToList();

    public string ItemsSummary => string.Join(", ", ItemUses.Select(u => $"{u.Quantity} × {u.Item.Name}"));

    public void SetItemQuantity(Guid itemId, int quantity)
    {
        // Ordre conservé : modifier une quantité ne déplace pas la ligne.
        var exists = mission.Items.Any(u => u.ItemId == itemId);
        mission.Items = quantity <= 0 ? mission.Items.Where(u => u.ItemId != itemId).ToList()
            : exists ? mission.Items.Select(u => u.ItemId == itemId ? u with { Quantity = quantity } : u).ToList()
            : [.. mission.Items, new MissionItemUse(itemId, quantity)];
        RefreshItems();
        owner.OnScheduleChanged();
    }

    public void RefreshItems()
    {
        OnPropertyChanged(nameof(ItemUses));
        OnPropertyChanged(nameof(ItemsSummary));
    }

    /// <summary>À appeler quand l'effectif des équipes affectées change.</summary>
    public void RefreshPlayers() => OnPropertyChanged(nameof(PlayersText));

    private void OnMaxPlayersChanged()
    {
        OnPropertyChanged(nameof(HasMaxPlayers));
        OnPropertyChanged(nameof(MaxPlayers));
        OnPropertyChanged(nameof(PlayersText));
        owner.OnScheduleChanged();
    }

    public IReadOnlyList<Guid> PredecessorIds => mission.PredecessorIds;

    // ----- Résultat (saisi par l'orga, un seul pour toutes les équipes) et score -----

    public MissionResultOption Result
    {
        get => MissionResultOption.Of(mission.Result);
        set
        {
            if (value is null || value.Value == mission.Result)
                return;
            mission.Result = value.Value;
            OnResultChanged();
        }
    }

    /// <summary>✔, ◐, ✘ ou rien (non évaluée).</summary>
    public string ResultSymbol => MissionResults.Symbol(mission.Result);

    public bool IsEvaluated => mission.Result != MissionResult.NotEvaluated;

    public bool IsSuccess => mission.Result == MissionResult.Success;

    public bool IsPartial => mission.Result == MissionResult.Partial;

    public bool IsFailure => mission.Result == MissionResult.Failure;

    /// <summary>« +10 pts » une fois la mission évaluée.</summary>
    public string EarnedPointsText => IsEvaluated ? $"{MissionResults.Points(mission):+0;-0;0} pts" : "";

    private CommunityToolkit.Mvvm.Input.RelayCommand<MissionResult>? _setResult;

    /// <summary>Choisit le résultat (bouton du suivi) ; un second clic sur le même résultat l'annule.</summary>
    public CommunityToolkit.Mvvm.Input.RelayCommand<MissionResult> SetResultCommand => _setResult ??= new(result =>
        Result = MissionResultOption.Of(result == mission.Result ? MissionResult.NotEvaluated : result));

    public string ResultNotes
    {
        get => mission.ResultNotes;
        set => SetProperty(mission.ResultNotes, value, mission, (m, v) => m.ResultNotes = v);
    }

    public decimal? SuccessPoints
    {
        get => mission.SuccessPoints;
        set => SetPoints(mission.SuccessPoints, value, v => mission.SuccessPoints = v);
    }

    public decimal? PartialPoints
    {
        get => mission.PartialPoints;
        set => SetPoints(mission.PartialPoints, value, v => mission.PartialPoints = v);
    }

    public decimal? FailurePoints
    {
        get => mission.FailurePoints;
        set => SetPoints(mission.FailurePoints, value, v => mission.FailurePoints = v);
    }

    private void SetPoints(int current, decimal? value, Action<int> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var points = (int)(value ?? 0);
        if (points == current)
            return;
        apply(points);
        OnPropertyChanged(name);
        owner.OnResultChanged();
    }

    private void OnResultChanged()
    {
        OnPropertyChanged(nameof(Result));
        OnPropertyChanged(nameof(ResultSymbol));
        OnPropertyChanged(nameof(IsEvaluated));
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsPartial));
        OnPropertyChanged(nameof(IsFailure));
        OnPropertyChanged(nameof(EarnedPointsText));
        owner.OnResultChanged();
    }

    // ----- Condition : mission jouée selon le résultat d'une autre -----

    public MissionConditionOption Condition
    {
        get => MissionConditionOption.Of(mission.Condition);
        set
        {
            if (value is null || value.Value == mission.Condition)
                return;
            mission.Condition = value.Value;
            OnConditionChanged();
        }
    }

    public MissionViewModel? ConditionMission
    {
        get => owner.Missions.FirstOrDefault(m => m.Model.Id == mission.ConditionMissionId);
        set
        {
            if (value?.Model.Id == mission.ConditionMissionId || value == this)
                return;
            mission.ConditionMissionId = value?.Model.Id;
            OnConditionChanged();
        }
    }

    public bool HasCondition => mission.Condition != MissionCondition.None;

    /// <summary>« Si « Assaut du pont » réussie » : affiché sur la frise.</summary>
    public string ConditionText => HasCondition && ConditionMission is { } other
        ? L.F("si_x_x", other.Name, MissionResults.ConditionLabel(mission.Condition).ToLower(L.Culture))
        : "";

    private void OnConditionChanged()
    {
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ConditionMission));
        OnPropertyChanged(nameof(HasCondition));
        OnPropertyChanged(nameof(ConditionText));
        owner.OnScheduleChanged();
        owner.OnResultChanged();
    }

    public IReadOnlyList<string> Issues
    {
        get => _issues;
        set
        {
            if (_issues.SequenceEqual(value))
                return;

            _issues = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IssuesText));
        }
    }

    public bool HasIssues => _issues.Count > 0;

    public string IssuesText => string.Join(Environment.NewLine, _issues.Select(i => "⚠ " + i));

    public void SetTeam(Guid teamId, bool assigned)
    {
        if (assigned == mission.TeamIds.Contains(teamId))
            return;

        mission.TeamIds = assigned ? [.. mission.TeamIds, teamId] : mission.TeamIds.Where(id => id != teamId).ToList();
        OnPropertyChanged(nameof(TeamIds));
        OnPropertyChanged(nameof(TeamsText));
        OnPropertyChanged(nameof(PlayersText));
        owner.OnScheduleChanged();
    }

    /// <summary>Remplace une équipe par une autre (glisser-déposer d'une colonne à l'autre sur la frise).</summary>
    public void ReplaceTeam(Guid oldTeamId, Guid newTeamId)
    {
        if (oldTeamId == newTeamId || mission.TeamIds.Contains(newTeamId))
            return;

        mission.TeamIds = mission.TeamIds.Select(id => id == oldTeamId ? newTeamId : id).ToList();
        OnPropertyChanged(nameof(TeamIds));
        OnPropertyChanged(nameof(TeamsText));
        OnPropertyChanged(nameof(PlayersText));
        owner.OnScheduleChanged();
    }

    public void SetPredecessor(Guid missionId, bool required)
    {
        if (required == mission.PredecessorIds.Contains(missionId))
            return;

        mission.PredecessorIds = required
            ? [.. mission.PredecessorIds, missionId]
            : mission.PredecessorIds.Where(id => id != missionId).ToList();
        OnPropertyChanged(nameof(PredecessorIds));
        owner.OnScheduleChanged();
    }

    public void RefreshPoints()
    {
        OnPropertyChanged(nameof(MissionPoints));
        OnPropertyChanged(nameof(HasMissionPoints));
    }

    public void RefreshZone()
    {
        OnPropertyChanged(nameof(Zone));
        OnPropertyChanged(nameof(ZoneName));
        OnPropertyChanged(nameof(Color));
    }

    private void OnTimesChanged()
    {
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndMinutes));
        OnPropertyChanged(nameof(DurationMinutes));
        OnPropertyChanged(nameof(TimeRangeText));
    }

    private bool SetScheduleProperty<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;

        apply(value);
        OnPropertyChanged(name);
        owner.OnScheduleChanged();
        return true;
    }
}
