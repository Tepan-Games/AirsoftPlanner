using System.Globalization;
using System.Text.RegularExpressions;

namespace AirsoftPlanner.Core.Domain;

/// <summary>Coordonnées GPS en degrés décimaux (WGS 84).</summary>
public readonly partial record struct GeoPoint(double Latitude, double Longitude)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;

    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:0.000000}, {Longitude:0.000000}");

    /// <summary>
    /// Lit des coordonnées « latitude, longitude » telles que copiées depuis Google Maps,
    /// Géoportail ou un GPS : « 48.8566, 2.3522 », « 48,8566 2,3522 », « 48.8566;2.3522 »...
    /// </summary>
    public static bool TryParse(string? text, out GeoPoint point)
    {
        point = default;
        var match = CoordinatesPattern().Match(text ?? "");
        if (!match.Success)
            return false;

        var latitude = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var longitude = double.Parse(match.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        point = new GeoPoint(latitude, longitude);
        return point.IsValid;
    }

    [GeneratedRegex(@"^\s*([-+]?\d+(?:[.,]\d+)?)\s*(?:[,;]\s*|\s+)([-+]?\d+(?:[.,]\d+)?)\s*$")]
    private static partial Regex CoordinatesPattern();
}
