using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

public record CoordinateFormatOption(CoordinateFormat Value, string Label, string ShortLabel)
{
    public static IReadOnlyList<CoordinateFormatOption> All { get; } =
    [
        new(CoordinateFormat.Utm, "UTM (31T 448251 5411952)", "UTM"),
        new(CoordinateFormat.DecimalDegrees, L.T("degres_decimaux_48_858370_n"), L.T("degres_decimaux_2")),
        new(CoordinateFormat.DegreesMinutesSeconds, L.T("degres_minutes_secondes_48_51_30_n"), L.T("degres_min_sec")),
    ];

    public static CoordinateFormatOption Of(CoordinateFormat format) => All.First(o => o.Value == format);

    public override string ToString() => Label;
}
