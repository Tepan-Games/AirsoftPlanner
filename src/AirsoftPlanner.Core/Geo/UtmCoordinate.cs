using System.Globalization;
using System.Text.RegularExpressions;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Geo;

/// <summary>
/// Coordonnée UTM (WGS 84), par exemple « 31T 448251 5411952 ».
/// La bande de latitude (C à X) indique l'hémisphère : N et au-delà = nord.
/// </summary>
public readonly partial record struct UtmCoordinate(int Zone, char Band, double Easting, double Northing)
{
    private const string Bands = "CDEFGHJKLMNPQRSTUVWX";

    // Ellipsoïde WGS 84 et paramètres UTM.
    private const double SemiMajorAxis = 6_378_137.0;
    private const double Flattening = 1 / 298.257223563;
    private const double ScaleFactor = 0.9996;
    private const double FalseEasting = 500_000;
    private const double SouthFalseNorthing = 10_000_000;

    // Séries de Krüger (précision millimétrique dans la zone UTM).
    private static readonly double N = Flattening / (2 - Flattening);
    private static readonly double A = SemiMajorAxis / (1 + N) * (1 + N * N / 4 + Math.Pow(N, 4) / 64);
    private static readonly double[] Alpha =
    [
        N / 2 - 2 * N * N / 3 + 5 * Math.Pow(N, 3) / 16,
        13 * N * N / 48 - 3 * Math.Pow(N, 3) / 5,
        61 * Math.Pow(N, 3) / 240,
    ];
    private static readonly double[] Beta =
    [
        N / 2 - 2 * N * N / 3 + 37 * Math.Pow(N, 3) / 96,
        N * N / 48 + Math.Pow(N, 3) / 15,
        17 * Math.Pow(N, 3) / 480,
    ];
    private static readonly double[] Delta =
    [
        2 * N - 2 * N * N / 3 - 2 * Math.Pow(N, 3),
        7 * N * N / 3 - 8 * Math.Pow(N, 3) / 5,
        56 * Math.Pow(N, 3) / 15,
    ];

    public bool IsNorthernHemisphere => Band >= 'N';

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Zone}{Band} {Math.Round(Easting):0} {Math.Round(Northing):0}");

    /// <summary>Zone UTM d'un point, avec les exceptions de la Norvège et du Svalbard.</summary>
    public static int ZoneOf(GeoPoint point)
    {
        var (lat, lon) = (point.Latitude, point.Longitude);
        if (lat is >= 56 and < 64 && lon is >= 3 and < 12)
            return 32;
        if (lat is >= 72 and <= 84 && lon >= 0 && lon < 42)
            return lon switch { < 9 => 31, < 21 => 33, < 33 => 35, _ => 37 };
        return Math.Clamp((int)Math.Floor((lon + 180) / 6) + 1, 1, 60);
    }

    public static UtmCoordinate FromGeo(GeoPoint point) => FromGeo(point, ZoneOf(point));

    /// <summary>Convertit dans une zone imposée (utile pour un quadrillage continu sur un terrain à cheval sur deux zones).</summary>
    public static UtmCoordinate FromGeo(GeoPoint point, int zone)
    {
        var latitude = Math.Clamp(point.Latitude, -80, 84);
        var band = Bands[Math.Clamp((int)Math.Floor((latitude + 80) / 8), 0, Bands.Length - 1)];

        var phi = point.Latitude * Math.PI / 180;
        var deltaLambda = (point.Longitude - CentralMeridian(zone)) * Math.PI / 180;
        var twoRootN = 2 * Math.Sqrt(N) / (1 + N);
        var t = Math.Sinh(Math.Atanh(Math.Sin(phi)) - twoRootN * Math.Atanh(twoRootN * Math.Sin(phi)));
        var xiPrime = Math.Atan(t / Math.Cos(deltaLambda));
        var etaPrime = Math.Atanh(Math.Sin(deltaLambda) / Math.Sqrt(1 + t * t));

        double xi = xiPrime, eta = etaPrime;
        for (var j = 1; j <= 3; j++)
        {
            xi += Alpha[j - 1] * Math.Sin(2 * j * xiPrime) * Math.Cosh(2 * j * etaPrime);
            eta += Alpha[j - 1] * Math.Cos(2 * j * xiPrime) * Math.Sinh(2 * j * etaPrime);
        }

        var easting = FalseEasting + ScaleFactor * A * eta;
        var northing = ScaleFactor * A * xi + (point.Latitude < 0 ? SouthFalseNorthing : 0);
        return new UtmCoordinate(zone, band, easting, northing);
    }

    public GeoPoint ToGeo()
    {
        var xi = (Northing - (IsNorthernHemisphere ? 0 : SouthFalseNorthing)) / (ScaleFactor * A);
        var eta = (Easting - FalseEasting) / (ScaleFactor * A);

        double xiPrime = xi, etaPrime = eta;
        for (var j = 1; j <= 3; j++)
        {
            xiPrime -= Beta[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            etaPrime -= Beta[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
        }

        var chi = Math.Asin(Math.Sin(xiPrime) / Math.Cosh(etaPrime));
        var phi = chi;
        for (var j = 1; j <= 3; j++)
            phi += Delta[j - 1] * Math.Sin(2 * j * chi);

        var lambda = Math.Atan(Math.Sinh(etaPrime) / Math.Cos(xiPrime));
        return new GeoPoint(phi * 180 / Math.PI, CentralMeridian(Zone) + lambda * 180 / Math.PI);
    }

    /// <summary>Lit « 31T 448251 5411952 », « 31 T 448251,5 5411952,3 », « 31T448251 5411952 »...</summary>
    public static bool TryParse(string? text, out UtmCoordinate coordinate)
    {
        coordinate = default;
        var match = UtmPattern().Match(text ?? "");
        if (!match.Success)
            return false;

        var zone = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var band = char.ToUpperInvariant(match.Groups[2].Value[0]);
        if (zone is < 1 or > 60 || !Bands.Contains(band))
            return false;

        var easting = double.Parse(match.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var northing = double.Parse(match.Groups[4].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        if (easting is < 100_000 or > 900_000 || northing is < 0 or > 10_000_000)
            return false;

        coordinate = new UtmCoordinate(zone, band, easting, northing);
        return true;
    }

    private static double CentralMeridian(int zone) => zone * 6 - 183;

    [GeneratedRegex(@"^\s*(\d{1,2})\s*([A-Za-z])\s*[,;]?\s*(\d{5,7}(?:[.,]\d+)?)\s*(?:[,;]\s*|\s+)(\d{5,8}(?:[.,]\d+)?)\s*(?:m)?\s*$")]
    private static partial Regex UtmPattern();
}
