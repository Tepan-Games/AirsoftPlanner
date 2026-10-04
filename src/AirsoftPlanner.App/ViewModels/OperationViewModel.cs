using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Édition des informations générales d'une OP.</summary>
public class OperationViewModel : ViewModelBase
{
    private readonly Operation operation;

    public OperationViewModel(Operation operation)
    {
        this.operation = operation;
        AppSettings.CoordinateFormatChanged += () => OnPropertyChanged(nameof(CoordinateFormat));
    }

    public string Name
    {
        get => operation.Name;
        set => SetProperty(operation.Name, value, operation, (o, v) => o.Name = v);
    }

    public string Location
    {
        get => operation.Location;
        set => SetProperty(operation.Location, value, operation, (o, v) => o.Location = v);
    }

    public string Description
    {
        get => operation.Description;
        set => SetProperty(operation.Description, value, operation, (o, v) => o.Description = v);
    }

    /// <summary>
    /// Date de début. La changer déplace toute l'OP (même durée) : les missions, exprimées par rapport
    /// au premier jour, suivent.
    /// </summary>
    public DateTime? StartDate
    {
        get => Day;
        set
        {
            if (value is not { } date || date.Date == Day)
                return;

            var shift = date.Date - Day;
            operation.StartsAt += shift;
            operation.EndsAt += shift;
            OnScheduleChanged();
        }
    }

    public string StartTimeText
    {
        get => operation.StartsAt.LocalDateTime.ToString("HH:mm");
        set
        {
            if (!MissionTime.TryParse(value, out var minutes) || minutes >= MissionTime.MinutesPerDay)
                throw new FormatException(L.T("heure_non_reconnue_ex_09_00"));

            var start = Day.AddMinutes(minutes);
            if (start >= operation.EndsAt.LocalDateTime)
                throw new FormatException(L.T("le_debut_doit_preceder_la_fin"));
            operation.StartsAt = new DateTimeOffset(start);
            OnScheduleChanged();
        }
    }

    public DateTime? EndDate
    {
        get => operation.EndsAt.LocalDateTime.Date;
        set
        {
            if (value is not { } date || date.Date == EndDate)
                return;

            var end = date.Date + operation.EndsAt.LocalDateTime.TimeOfDay;
            if (end <= operation.StartsAt.LocalDateTime)
                throw new FormatException(L.T("la_fin_doit_suivre_le_debut"));
            operation.EndsAt = new DateTimeOffset(end);
            OnScheduleChanged();
        }
    }

    public string EndTimeText
    {
        get => operation.EndsAt.LocalDateTime.ToString("HH:mm");
        set
        {
            if (!MissionTime.TryParse(value, out var minutes) || minutes >= MissionTime.MinutesPerDay)
                throw new FormatException(L.T("heure_non_reconnue_ex_18_00"));

            var end = operation.EndsAt.LocalDateTime.Date.AddMinutes(minutes);
            if (end <= operation.StartsAt.LocalDateTime)
                throw new FormatException(L.T("la_fin_doit_suivre_le_debut"));
            operation.EndsAt = new DateTimeOffset(end);
            OnScheduleChanged();
        }
    }

    public string DurationText
    {
        get
        {
            var duration = operation.EndsAt - operation.StartsAt;
            return duration.TotalHours < 24
                ? L.F("duree_x", MissionTime.FormatDuration((int)duration.TotalMinutes))
                : L.F("duree_x_j_x_h_x_jours_de_jeu", (int)duration.TotalDays, duration.Hours, Math.Ceiling((operation.EndsAt.LocalDateTime.Date - Day).TotalDays) + 1);
        }
    }

    /// <summary>Premier jour de l'OP : les heures des missions sont comptées depuis minuit ce jour-là.</summary>
    public DateTime Day => operation.StartsAt.LocalDateTime.Date;

    /// <summary>Début de l'OP en minutes depuis minuit le premier jour.</summary>
    public int StartMinutes => (int)Math.Round((operation.StartsAt.LocalDateTime - Day).TotalMinutes);

    /// <summary>Fin de l'OP en minutes depuis minuit le premier jour.</summary>
    public int EndMinutes => (int)Math.Round((operation.EndsAt.LocalDateTime - Day).TotalMinutes);

    public decimal? WalkingSpeedKmh
    {
        get => (decimal)operation.WalkingSpeedKmh;
        set => SetProperty(operation.WalkingSpeedKmh, (double)Math.Clamp(value ?? 3, 0.5m, 20m), operation, (o, v) => o.WalkingSpeedKmh = v);
    }

    /// <summary>Convertit une heure de l'OP (minutes depuis minuit le premier jour) en date et heure.</summary>
    public DateTime ToDateTime(double minutes) => Day.AddMinutes(minutes);

    /// <summary>Convertit une date et heure en minutes depuis minuit le premier jour.</summary>
    public double ToMinutes(DateTime time) => (time - Day).TotalMinutes;

    private void OnScheduleChanged()
    {
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(Day));
        OnPropertyChanged(nameof(StartMinutes));
        OnPropertyChanged(nameof(EndMinutes));
    }

    public string OrganizerName
    {
        get => operation.OrganizerName;
        set => SetProperty(operation.OrganizerName, value, operation, (o, v) => o.OrganizerName = v);
    }

    public decimal? VehicleSpeedKmh
    {
        get => (decimal)operation.VehicleSpeedKmh;
        set => SetProperty(operation.VehicleSpeedKmh, (double)Math.Clamp(value ?? 25, 1m, 120m), operation, (o, v) => o.VehicleSpeedKmh = v);
    }

    public string OrgaRadioFrequency
    {
        get => operation.OrgaRadioFrequency;
        set => SetProperty(operation.OrgaRadioFrequency, value, operation, (o, v) => o.OrgaRadioFrequency = v);
    }

    public string EmergencyPhone
    {
        get => operation.EmergencyPhone;
        set => SetProperty(operation.EmergencyPhone, value, operation, (o, v) => o.EmergencyPhone = v);
    }

    public IReadOnlyList<CoordinateFormatOption> CoordinateFormats => CoordinateFormatOption.All;

    /// <summary>Format d'affichage à l'écran : paramètre de l'application (barre du haut), commun à toutes les OP.</summary>
    public CoordinateFormat CoordinateFormat => AppSettings.Current.CoordinateFormat;

    /// <summary>Format des coordonnées dans les documents imprimés de cette OP (ordres de mission...).</summary>
    public CoordinateFormatOption SelectedCoordinateFormat
    {
        get => CoordinateFormats.First(f => f.Value == operation.CoordinateFormat);
        set
        {
            if (value is null || value.Value == operation.CoordinateFormat)
                return;

            operation.CoordinateFormat = value.Value;
            OnPropertyChanged();
        }
    }
}
