using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Geo;

/// <summary>Distances et appartenance à une zone, à l'échelle d'un terrain.</summary>
public static class GeoMath
{
    private const double EarthRadius = 6_371_000;

    /// <summary>Distance en mètres entre deux points (formule de haversine).</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadius * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>Distance en mètres d'un point à une zone : 0 à l'intérieur d'un polygone.</summary>
    public static double DistanceToZone(GeoPoint point, Zone zone)
    {
        if (zone.Points.Count == 0)
            return double.NaN;
        if (zone.Kind == ZoneKind.Point || zone.Points.Count < 3)
            return zone.Points.Min(p => DistanceMeters(point, p));
        if (Contains(zone.Points, point))
            return 0;

        // Projection locale en mètres autour du point, puis distance aux côtés du polygone.
        var local = zone.Points.Select(p => ToLocal(point, p)).ToList();
        var best = double.MaxValue;
        for (var i = 0; i < local.Count; i++)
            best = Math.Min(best, DistanceToSegment(local[i], local[(i + 1) % local.Count]));
        return best;
    }

    public static bool Contains(IReadOnlyList<GeoPoint> polygon, GeoPoint point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (pi, pj) = (polygon[i], polygon[j]);
            if ((pi.Latitude > point.Latitude) != (pj.Latitude > point.Latitude)
                && point.Longitude < (pj.Longitude - pi.Longitude) * (point.Latitude - pi.Latitude) / (pj.Latitude - pi.Latitude) + pi.Longitude)
                inside = !inside;
        }

        return inside;
    }

    public static GeoPoint Centroid(IReadOnlyList<GeoPoint> points) =>
        new(points.Average(p => p.Latitude), points.Average(p => p.Longitude));

    private static (double X, double Y) ToLocal(GeoPoint origin, GeoPoint point) =>
        (ToRadians(point.Longitude - origin.Longitude) * EarthRadius * Math.Cos(ToRadians(origin.Latitude)),
         ToRadians(point.Latitude - origin.Latitude) * EarthRadius);

    /// <summary>Distance de l'origine (0, 0) au segment [a, b].</summary>
    private static double DistanceToSegment((double X, double Y) a, (double X, double Y) b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(-(a.X * dx + a.Y * dy) / lengthSquared, 0, 1);
        var (x, y) = (a.X + t * dx, a.Y + t * dy);
        return Math.Sqrt(x * x + y * y);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
