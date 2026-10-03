using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Tests;

public class GeoPointTests
{
    [Theory]
    [InlineData("48.8566, 2.3522")]
    [InlineData("48.8566,2.3522")]
    [InlineData("48,8566, 2,3522")]
    [InlineData("48,8566 2,3522")]
    [InlineData("  48.8566 ; 2.3522  ")]
    public void TryParse_accepts_common_formats(string text)
    {
        Assert.True(GeoPoint.TryParse(text, out var point));
        Assert.Equal(48.8566, point.Latitude, 9);
        Assert.Equal(2.3522, point.Longitude, 9);
    }

    [Fact]
    public void TryParse_accepts_negative_coordinates()
    {
        Assert.True(GeoPoint.TryParse("-33.8688, -151.2093", out var point));
        Assert.Equal(new GeoPoint(-33.8688, -151.2093), point);
    }

    [Theory]
    [InlineData("")]
    [InlineData("48.8566")]
    [InlineData("Paris")]
    [InlineData("95, 2")]
    public void TryParse_rejects_invalid_input(string text)
    {
        Assert.False(GeoPoint.TryParse(text, out _));
    }

    [Fact]
    public void ToString_round_trips_through_TryParse()
    {
        var point = new GeoPoint(45.123456, 5.654321);

        Assert.True(GeoPoint.TryParse(point.ToString(), out var parsed));
        Assert.Equal(point, parsed);
    }
}
