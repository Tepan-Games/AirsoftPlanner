using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.App.ViewModels;

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
                throw new FormatException("Heure non reconnue (ex. 10:30).");
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

    /// <summary>Couleur de la mission sur la frise : celle de sa zone.</summary>
    public string Color => Zone?.Color ?? DefaultColor;

    public IReadOnlyList<Guid> TeamIds => mission.TeamIds;

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
            return mission.MaxPlayers is { } max ? $"{assigned} joueur(s) affecté(s) sur {max} maximum" : $"{assigned} joueur(s) affecté(s)";
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
