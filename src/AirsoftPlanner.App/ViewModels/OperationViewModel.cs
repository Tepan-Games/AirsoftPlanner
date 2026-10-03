using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

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
        get => operation.StartsAt.LocalDateTime.Date;
        set
        {
            if (value is not { } date || date.Date == Date)
                return;

            var duration = operation.EndsAt - operation.StartsAt;
            operation.StartsAt = new DateTimeOffset(date.Date + operation.StartsAt.LocalDateTime.TimeOfDay);
            operation.EndsAt = operation.StartsAt + duration;
            OnPropertyChanged();
        }
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
