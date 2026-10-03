using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Geo;

/// <summary>Tuiles carrées en projection Web Mercator (grille « PM » de l'IGN, OpenStreetMap...).</summary>
public static class TileGrid
{
    public const int TileSize = 256;

    public static (double X, double Y) ToTile(GeoPoint point, int zoom)
    {
        var scale = Math.Pow(2, zoom);
        var phi = point.Latitude * Math.PI / 180;
        var x = (point.Longitude + 180) / 360 * scale;
        var y = (1 - Math.Log(Math.Tan(phi) + 1 / Math.Cos(phi)) / Math.PI) / 2 * scale;
        return (x, y);
    }

    public static GeoPoint FromTile(double x, double y, int zoom)
    {
        var scale = Math.Pow(2, zoom);
        var longitude = x / scale * 360 - 180;
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / scale))) * 180 / Math.PI;
        return new GeoPoint(latitude, longitude);
    }

    /// <summary>Tuiles à télécharger pour couvrir une zone à un niveau de zoom.</summary>
    public static TilePlan Plan(GeoBounds area, int zoom)
    {
        var (minX, minY) = ToTile(new GeoPoint(area.North, area.West), zoom);
        var (maxX, maxY) = ToTile(new GeoPoint(area.South, area.East), zoom);
        return new TilePlan(zoom, (int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Floor(maxX), (int)Math.Floor(maxY));
    }

    /// <summary>Zoom le plus précis (jusqu'à <paramref name="maxZoom"/>) dont l'image assemblée tient dans la taille donnée.</summary>
    public static TilePlan BestPlan(GeoBounds area, int maxZoom, int maxImageSide)
    {
        for (var zoom = maxZoom; zoom > 0; zoom--)
        {
            var plan = Plan(area, zoom);
            if (plan.PixelWidth <= maxImageSide && plan.PixelHeight <= maxImageSide)
                return plan;
        }

        return Plan(area, 1);
    }
}

/// <summary>Rectangle de tuiles (bornes incluses) et image assemblée correspondante.</summary>
public readonly record struct TilePlan(int Zoom, int MinX, int MinY, int MaxX, int MaxY)
{
    public int Columns => MaxX - MinX + 1;

    public int Rows => MaxY - MinY + 1;

    public int TileCount => Columns * Rows;

    public int PixelWidth => Columns * TileGrid.TileSize;

    public int PixelHeight => Rows * TileGrid.TileSize;

    /// <summary>
    /// Emprise GPS exacte de l'image assemblée. À l'échelle d'un terrain, l'écart entre
    /// Web Mercator et une interpolation linéaire en latitude reste inférieur au mètre.
    /// </summary>
    public GeoBounds Bounds
    {
        get
        {
            var northWest = TileGrid.FromTile(MinX, MinY, Zoom);
            var southEast = TileGrid.FromTile(MaxX + 1, MaxY + 1, Zoom);
            return new GeoBounds(northWest.Latitude, southEast.Latitude, northWest.Longitude, southEast.Longitude);
        }
    }

    /// <summary>Taille d'un pixel au sol, en mètres.</summary>
    public double MetersPerPixel
    {
        get
        {
            var (width, _) = Bounds.SizeInMeters();
            return width / PixelWidth;
        }
    }

    public IEnumerable<(int X, int Y)> Tiles()
    {
        for (var y = MinY; y <= MaxY; y++)
        for (var x = MinX; x <= MaxX; x++)
            yield return (x, y);
    }
}
