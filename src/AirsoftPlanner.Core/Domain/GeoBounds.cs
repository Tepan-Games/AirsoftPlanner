namespace AirsoftPlanner.Core.Domain;

/// <summary>
/// Rectangle GPS couvert par la carte du terrain (image orientée nord en haut).
/// À l'échelle d'un terrain d'airsoft (quelques km), une interpolation linéaire suffit.
/// </summary>
public readonly record struct GeoBounds(double North, double South, double West, double East)
{
    public bool IsValid => North > South && East > West;

    /// <summary>Position relative dans le rectangle : (0, 0) en haut à gauche, (1, 1) en bas à droite.</summary>
    public (double X, double Y) ToRelative(GeoPoint point) =>
        ((point.Longitude - West) / (East - West), (North - point.Latitude) / (North - South));

    public GeoPoint FromRelative(double x, double y) =>
        new(North - y * (North - South), West + x * (East - West));

    /// <summary>Largeur et hauteur approximatives en mètres.</summary>
    public (double Width, double Height) SizeInMeters()
    {
        const double metersPerDegreeLatitude = 111_320;
        var middleLatitude = (North + South) / 2 * Math.PI / 180;
        return ((East - West) * metersPerDegreeLatitude * Math.Cos(middleLatitude),
                (North - South) * metersPerDegreeLatitude);
    }

    /// <summary>Rectangle englobant des points, agrandi d'une marge relative.</summary>
    public static GeoBounds Around(IReadOnlyCollection<GeoPoint> points, double margin = 0.1)
    {
        if (points.Count == 0)
            throw new ArgumentException("Au moins un point est nécessaire.", nameof(points));

        var north = points.Max(p => p.Latitude);
        var south = points.Min(p => p.Latitude);
        var east = points.Max(p => p.Longitude);
        var west = points.Min(p => p.Longitude);
        // Évite un rectangle plat quand il n'y a qu'un point (environ 100 m).
        var latitudePadding = Math.Max((north - south) * margin, 0.0005);
        var longitudePadding = Math.Max((east - west) * margin, 0.0007);
        return new GeoBounds(north + latitudePadding, south - latitudePadding,
                             west - longitudePadding, east + longitudePadding);
    }
}
