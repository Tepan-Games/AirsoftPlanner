using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.App.ViewModels;

public class FactionViewModel(Faction faction) : ViewModelBase
{
    private Func<IEnumerable<TeamViewModel>> _allTeams = () => [];

    public Faction Model => faction;

    public string Name
    {
        get => faction.Name;
        set => SetProperty(faction.Name, value, faction, (f, v) => f.Name = v);
    }

    public string Color
    {
        get => faction.Color;
        set => SetProperty(faction.Color, value, faction, (f, v) => f.Color = v);
    }

    public string Description
    {
        get => faction.Description;
        set => SetProperty(faction.Description, value, faction, (f, v) => f.Description = v);
    }

    public decimal? MinPlayers
    {
        get => faction.MinPlayers;
        set
        {
            if (SetProperty(faction.MinPlayers, (int)(value ?? 0), faction, (f, v) => f.MinPlayers = v))
                RefreshStaffing();
        }
    }

    public decimal? MaxPlayers
    {
        get => faction.MaxPlayers;
        set
        {
            if (SetProperty(faction.MaxPlayers, (int)(value ?? 0), faction, (f, v) => f.MaxPlayers = v))
                RefreshStaffing();
        }
    }

    public bool HasArmband
    {
        get => faction.ArmbandColor.Length > 0;
        set
        {
            if (value == HasArmband)
                return;

            faction.ArmbandColor = value ? faction.Color : "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArmbandColor));
        }
    }

    /// <summary>Couleur du brassard, ou null s'il n'y en a pas.</summary>
    public string? ArmbandColor
    {
        get => faction.ArmbandColor.Length > 0 ? faction.ArmbandColor : null;
        set
        {
            if (value is null || value == faction.ArmbandColor)
                return;

            faction.ArmbandColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasArmband));
        }
    }

    public string Uniform
    {
        get => faction.Uniform;
        set => SetProperty(faction.Uniform, value, faction, (f, v) => f.Uniform = v);
    }

    public string RadioFrequency
    {
        get => faction.RadioFrequency;
        set => SetProperty(faction.RadioFrequency, value, faction, (f, v) => f.RadioFrequency = v);
    }

    public IReadOnlyList<TeamViewModel> Teams => _allTeams().Where(t => t.Model.FactionId == faction.Id).ToList();

    public TeamViewModel? CommandTeam
    {
        get => Teams.FirstOrDefault(t => t.Model.Id == faction.CommandTeamId);
        set
        {
            if (value?.Model.Id == faction.CommandTeamId)
                return;

            faction.CommandTeamId = value?.Model.Id;
            OnPropertyChanged();
        }
    }

    /// <summary>Équipes pré-inscrites ou confirmées (les équipes en attente ou annulées ne comptent pas).</summary>
    public IReadOnlyList<TeamViewModel> PlayingTeams => Teams.Where(t => t.IsPlaying).ToList();

    public int PlayerCount => PlayingTeams.Sum(t => t.Size);

    public int WaitingCount => Teams.Count(t => t.Status.Value == AirsoftPlanner.Core.Registration.RegistrationStatus.WaitingList);

    public bool IsUnderstaffed => faction.MinPlayers > 0 && PlayerCount < faction.MinPlayers;

    public bool IsOverstaffed => faction.MaxPlayers > 0 && PlayerCount > faction.MaxPlayers;

    public bool HasStaffingIssue => IsUnderstaffed || IsOverstaffed;

    public string StaffingText
    {
        get
        {
            var teams = PlayingTeams.Count;
            var limits = (faction.MinPlayers, faction.MaxPlayers) switch
            {
                (0, 0) => "",
                (var min, 0) => $" (min {min})",
                (0, var max) => $" (max {max})",
                var (min, max) => $" (min {min}, max {max})",
            };
            var status = IsUnderstaffed ? $" — il manque {faction.MinPlayers - PlayerCount} joueur(s)"
                : IsOverstaffed ? $" — {PlayerCount - faction.MaxPlayers} joueur(s) en trop"
                : "";
            var waiting = WaitingCount > 0 ? $" · {WaitingCount} équipe(s) en attente" : "";
            return $"{PlayerCount} joueur(s) dans {teams} équipe(s){limits}{status}{waiting}";
        }
    }

    public void AttachTeams(Func<IEnumerable<TeamViewModel>> allTeams)
    {
        _allTeams = allTeams;
        RefreshStaffing();
    }

    /// <summary>À appeler quand les équipes de la faction ou leur effectif changent.</summary>
    public void RefreshStaffing()
    {
        OnPropertyChanged(nameof(Teams));
        OnPropertyChanged(nameof(CommandTeam));
        OnPropertyChanged(nameof(PlayingTeams));
        OnPropertyChanged(nameof(PlayerCount));
        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(IsUnderstaffed));
        OnPropertyChanged(nameof(IsOverstaffed));
        OnPropertyChanged(nameof(HasStaffingIssue));
        OnPropertyChanged(nameof(StaffingText));
    }
}
