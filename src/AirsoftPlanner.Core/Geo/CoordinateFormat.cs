using System.Globalization;
using System.Text.RegularExpressions;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Geo;

public enum CoordinateFormat
{
    /// <summary>48,858370° N, 2,294481° E</summary>
    DecimalDegrees,

    /// <summary>48°51'30,1"N 2°17'40,1"E</summary>
    DegreesMinutesSeconds,

    /// <summary>31U 448251 5411952</summary>
    Utm,
}

/// <summary>Affichage et saisie de coordonnées dans tous les formats gérés.</summary>
public static partial class Coordinates
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    /// <summary>Ouest : « O » en français, espagnol et italien, « W » sinon.</summary>
    private static char West => AirsoftPlanner.Core.Localization.L.Code is "fr" or "es" or "it" ? 'O' : 'W';

    public static string Format(GeoPoint point, CoordinateFormat format) => format switch
    {
        CoordinateFormat.DecimalDegrees => string.Format(French, "{0:0.000000}° {1}, {2:0.000000}° {3}",
            Math.Abs(point.Latitude), point.Latitude >= 0 ? 'N' : 'S',
            Math.Abs(point.Longitude), point.Longitude >= 0 ? 'E' : West),
        CoordinateFormat.DegreesMinutesSeconds =>
            $"{FormatDms(point.Latitude, 'N', 'S')} {FormatDms(point.Longitude, 'E', West)}",
        CoordinateFormat.Utm => UtmCoordinate.FromGeo(point).ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Lit des coordonnées saisies dans n'importe quel format géré.</summary>
    public static bool TryParse(string? text, out GeoPoint point)
    {
        if (UtmCoordinate.TryParse(text, out var utm))
        {
            point = utm.ToGeo();
            return true;
        }

        return GeoPoint.TryParse(text, out point)
            || TryParseHemisphereDegrees(text, out point);
    }

    private static string FormatDms(double value, char positive, char negative)
    {
        var totalSeconds = Math.Round(Math.Abs(value) * 3600, 1);
        var degrees = (int)(totalSeconds / 3600);
        var minutes = (int)(totalSeconds % 3600 / 60);
        var seconds = totalSeconds % 60;
        return string.Format(French, "{0}°{1:00}'{2:00.0}\"{3}", degrees, minutes, seconds, value >= 0 ? positive : negative);
    }

    /// <summary>Formats avec hémisphère : degrés-minutes-secondes, degrés-minutes ou degrés décimaux.</summary>
    private static bool TryParseHemisphereDegrees(string? text, out GeoPoint point)
    {
        point = default;
        var matches = HemisphereComponent().Matches(text ?? "");
        if (matches.Count != 2)
            return false;

        double? latitude = null, longitude = null;
        foreach (Match match in matches)
        {
            var value = ParseNumber(match.Groups["deg"].Value)
                + ParseNumber(match.Groups["min"].Value) / 60
                + ParseNumber(match.Groups["sec"].Value) / 3600;
            switch (char.ToUpperInvariant(match.Groups["hem"].Value[0]))
            {
                case 'N': latitude = value; break;
                case 'S': latitude = -value; break;
                case 'E': longitude = value; break;
                default: longitude = -value; break; // O (ouest) ou W (west)
            }
        }

        if (latitude is null || longitude is null)
            return false;

        point = new GeoPoint(latitude.Value, longitude.Value);
        return point.IsValid;
    }

    private static double ParseNumber(string text) =>
        text.Length == 0 ? 0 : double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);

    [GeneratedRegex("""(?<deg>\d+(?:[.,]\d+)?)\s*°\s*(?:(?<min>\d+(?:[.,]\d+)?)\s*['′’]\s*)?(?:(?<sec>\d+(?:[.,]\d+)?)\s*(?:"|″|''|’’)\s*)?(?<hem>[NSEOWnseow])\b""")]
    private static partial Regex HemisphereComponent();
}
