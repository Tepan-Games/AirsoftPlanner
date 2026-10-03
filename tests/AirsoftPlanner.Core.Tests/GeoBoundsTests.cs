using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Tests;

public class GeoBoundsTests
{
    private static readonly GeoBounds Bounds = new(North: 48.86, South: 48.85, West: 2.33, East: 2.35);

    [Fact]
    public void Corners_map_to_relative_extremes()
    {
        Assert.Equal((0, 0), Bounds.ToRelative(new GeoPoint(48.86, 2.33)));
        var (x, y) = Bounds.ToRelative(new GeoPoint(48.85, 2.35));
        Assert.Equal(1, x, 9);
        Assert.Equal(1, y, 9);
    }

    [Fact]
    public void FromRelative_is_the_inverse_of_ToRelative()
    {
        var point = new GeoPoint(48.8537, 2.3412);
        var (x, y) = Bounds.ToRelative(point);
        var back = Bounds.FromRelative(x, y);

        Assert.Equal(point.Latitude, back.Latitude, 9);
        Assert.Equal(point.Longitude, back.Longitude, 9);
    }

    [Fact]
    public void Size_in_meters_is_plausible()
    {
        var (width, height) = Bounds.SizeInMeters();

        Assert.InRange(height, 1100, 1125); // 0,01° de latitude ≈ 1,11 km
        Assert.InRange(width, 1450, 1480); // 0,02° de longitude à 48,9° N ≈ 1,46 km
    }

    [Fact]
    public void Around_a_single_point_gives_a_usable_rectangle()
    {
        var bounds = GeoBounds.Around([new GeoPoint(45, 5)]);

        Assert.True(bounds.IsValid);
        Assert.Equal((0.5, 0.5), bounds.ToRelative(new GeoPoint(45, 5)));
    }
}
