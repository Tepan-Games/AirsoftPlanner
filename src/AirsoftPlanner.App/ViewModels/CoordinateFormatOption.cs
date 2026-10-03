using System.Collections.Generic;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.App.ViewModels;

public record CoordinateFormatOption(CoordinateFormat Value, string Label)
{
    public static IReadOnlyList<CoordinateFormatOption> All { get; } =
    [
        new(CoordinateFormat.Utm, "UTM (31T 448251 5411952)"),
        new(CoordinateFormat.DecimalDegrees, "Degrés décimaux (48,858370° N)"),
        new(CoordinateFormat.DegreesMinutesSeconds, "Degrés minutes secondes (48°51'30\"N)"),
    ];

    public override string ToString() => Label;
}
