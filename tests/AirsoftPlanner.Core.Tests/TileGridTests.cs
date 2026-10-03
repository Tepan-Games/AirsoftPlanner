using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Tests;

public class TileGridTests
{
    [Fact]
    public void Known_tile_is_found()
    {
        // Référence : formule des tuiles OpenStreetMap (zoom 16, Paris).
        var (x, y) = TileGrid.ToTile(new GeoPoint(48.8566, 2.3522), 16);

        Assert.Equal(33196, (int)x);
        Assert.Equal(22546, (int)y);
    }

    [Fact]
    public void FromTile_is_the_inverse_of_ToTile()
    {
        var point = new GeoPoint(45.188529, 5.724524);
        var (x, y) = TileGrid.ToTile(point, 17);
        var back = TileGrid.FromTile(x, y, 17);

        Assert.Equal(point.Latitude, back.Latitude, 1e-9);
        Assert.Equal(point.Longitude, back.Longitude, 1e-9);
    }

    [Fact]
    public void Plan_covers_the_requested_area()
    {
        var area = new GeoBounds(North: 45.20, South: 45.18, West: 5.70, East: 5.74);
        var plan = TileGrid.Plan(area, 16);
        var covered = plan.Bounds;

        Assert.True(covered.North >= area.North && covered.South <= area.South);
        Assert.True(covered.West <= area.West && covered.East >= area.East);
        Assert.Equal(plan.Columns * plan.Rows, plan.Tiles().Count());
    }

    [Fact]
    public void BestPlan_respects_the_maximum_image_size()
    {
        var area = new GeoBounds(North: 45.20, South: 45.18, West: 5.70, East: 5.74); // ≈ 3 km × 2 km
        var plan = TileGrid.BestPlan(area, maxZoom: 19, maxImageSide: 8192);

        Assert.InRange(plan.PixelWidth, 1, 8192);
        Assert.InRange(plan.PixelHeight, 1, 8192);
        Assert.True(TileGrid.Plan(area, plan.Zoom + 1).PixelWidth > 8192 || plan.Zoom == 19);
        Assert.InRange(plan.MetersPerPixel, 0.1, 1.0);
    }
}
