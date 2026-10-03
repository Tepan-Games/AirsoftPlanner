using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.App.ViewModels;

public record CoordinateFormatOption(CoordinateFormat Value, string Label, string ShortLabel)
{
    public static IReadOnlyList<CoordinateFormatOption> All { get; } =
    [
        new(CoordinateFormat.Utm, "UTM (31T 448251 5411952)", "UTM"),
        new(CoordinateFormat.DecimalDegrees, "Degrés décimaux (48,858370° N)", "Degrés décimaux"),
        new(CoordinateFormat.DegreesMinutesSeconds, "Degrés minutes secondes (48°51'30\"N)", "Degrés min. sec."),
    ];

    public static CoordinateFormatOption Of(CoordinateFormat format) => All.First(o => o.Value == format);

    public override string ToString() => Label;
}
