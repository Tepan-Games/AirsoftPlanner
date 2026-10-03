using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Édition des informations générales d'une OP.</summary>
public class OperationViewModel(Operation operation) : ViewModelBase
{
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

    public DateTime? Date
    {
        get => Day;
        set
        {
            if (value is not { } date || date.Date == Day)
                return;

            SetSchedule(date.Date, StartMinutes, EndMinutes);
            OnPropertyChanged();
        }
    }

    /// <summary>Début de l'OP en minutes depuis minuit le jour de l'OP.</summary>
    public int StartMinutes => (int)Math.Round((operation.StartsAt.LocalDateTime - Day).TotalMinutes);

    /// <summary>Fin de l'OP en minutes depuis minuit le jour de l'OP (au-delà de 24 h pour une OP de nuit).</summary>
    public int EndMinutes => (int)Math.Round((operation.EndsAt.LocalDateTime - Day).TotalMinutes);

    public string StartText
    {
        get => MissionTime.Format(StartMinutes);
        set
        {
            if (!MissionTime.TryParse(value, out var start) || start >= MissionTime.MinutesPerDay)
                throw new FormatException("Heure non reconnue (ex. 09:00).");

            var end = EndMinutes;
            if (end <= start)
                end += MissionTime.MinutesPerDay;
            SetSchedule(Day, start, end);
        }
    }

    /// <summary>Une heure de fin plus tôt que le début désigne le lendemain (OP de nuit).</summary>
    public string EndText
    {
        get => MissionTime.Format(EndMinutes);
        set
        {
            if (!MissionTime.TryParse(value, out var end))
                throw new FormatException("Heure non reconnue (ex. 18:00).");

            if (end <= StartMinutes)
                end += MissionTime.MinutesPerDay;
            SetSchedule(Day, StartMinutes, end);
        }
    }

    private DateTime Day => operation.StartsAt.LocalDateTime.Date;

    private void SetSchedule(DateTime day, int startMinutes, int endMinutes)
    {
        operation.StartsAt = new DateTimeOffset(day.AddMinutes(startMinutes));
        operation.EndsAt = new DateTimeOffset(day.AddMinutes(endMinutes));
        OnPropertyChanged(nameof(StartMinutes));
        OnPropertyChanged(nameof(EndMinutes));
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndText));
    }

    public IReadOnlyList<CoordinateFormatOption> CoordinateFormats => CoordinateFormatOption.All;

    public CoordinateFormat CoordinateFormat => operation.CoordinateFormat;

    public CoordinateFormatOption SelectedCoordinateFormat
    {
        get => CoordinateFormats.First(f => f.Value == operation.CoordinateFormat);
        set
        {
            if (value is null || value.Value == operation.CoordinateFormat)
                return;

            operation.CoordinateFormat = value.Value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CoordinateFormat));
        }
    }
}
