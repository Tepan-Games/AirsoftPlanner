using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Tests;

public class CoordinatesTests
{
    // Valeurs de référence calculées avec PROJ (pyproj), WGS 84.
    [Theory]
    [InlineData(48.858370, 2.294481, 31, 'U', 448250.577, 5411951.588)] // tour Eiffel
    [InlineData(45.188529, 5.724524, 31, 'T', 714028.374, 5007505.390)] // Grenoble
    [InlineData(43.6047, 1.4442, 31, 'T', 374439.168, 4829145.130)] // Toulouse
    [InlineData(-33.856784, 151.215297, 56, 'H', 334900.261, 6252290.522)] // Sydney
    [InlineData(40.5, -73.5, 18, 'T', 627103.087, 4484335.402)]
    [InlineData(60.5, 5.3, 32, 'V', 296817.425, 6712810.072)] // exception norvégienne
    public void Utm_conversion_matches_reference(double lat, double lon, int zone, char band, double easting, double northing)
    {
        var utm = UtmCoordinate.FromGeo(new GeoPoint(lat, lon));

        Assert.Equal(zone, utm.Zone);
        Assert.Equal(band, utm.Band);
        Assert.Equal(easting, utm.Easting, 0.01);
        Assert.Equal(northing, utm.Northing, 0.01);

        var back = utm.ToGeo();
        Assert.Equal(lat, back.Latitude, 1e-7);
        Assert.Equal(lon, back.Longitude, 1e-7);
    }

    [Theory]
    [InlineData("31U 448251 5411952")]
    [InlineData("31 U 448251 5411952")]
    [InlineData("31u 448250,6 5411951,6")]
    [InlineData("31U 448251, 5411952")]
    public void Utm_text_is_parsed(string text)
    {
        Assert.True(UtmCoordinate.TryParse(text, out var utm));
        Assert.Equal(31, utm.Zone);
        Assert.Equal('U', utm.Band);
        Assert.Equal(448251, utm.Easting, 0.5);
        Assert.Equal(5411952, utm.Northing, 0.5);
    }

    [Fact]
    public void Utm_is_formatted_to_the_meter()
    {
        Assert.Equal("31U 448251 5411952", Coordinates.Format(new GeoPoint(48.858370, 2.294481), CoordinateFormat.Utm));
    }

    [Fact]
    public void Decimal_degrees_are_formatted_in_french()
    {
        Assert.Equal("48,858370° N, 2,294481° E",
            Coordinates.Format(new GeoPoint(48.858370, 2.294481), CoordinateFormat.DecimalDegrees));
        Assert.Equal("33,856784° S, 70,600000° O",
            Coordinates.Format(new GeoPoint(-33.856784, -70.6), CoordinateFormat.DecimalDegrees));
    }

    [Fact]
    public void Degrees_minutes_seconds_are_formatted()
    {
        Assert.Equal("48°51'30,1\"N 2°17'40,1\"E",
            Coordinates.Format(new GeoPoint(48.858370, 2.294481), CoordinateFormat.DegreesMinutesSeconds));
    }

    [Theory]
    [InlineData("48°51'30.1\"N 2°17'40.1\"E")]
    [InlineData("48°51'30,1\"N, 2°17'40,1\"E")]
    [InlineData("48° 51′ 30.1″ N 2° 17′ 40.1″ E")]
    [InlineData("2°17'40.1\"E 48°51'30.1\"N")]
    [InlineData("48°51.502'N 2°17.668'E")]
    [InlineData("48.85837°N 2.29448°E")]
    [InlineData("48.858370, 2.294481")]
    [InlineData("31U 448251 5411952")]
    public void Any_supported_format_is_parsed(string text)
    {
        Assert.True(Coordinates.TryParse(text, out var point));
        Assert.Equal(48.85837, point.Latitude, 1e-4);
        Assert.Equal(2.29448, point.Longitude, 1e-4);
    }

    [Fact]
    public void West_longitude_accepts_o_and_w()
    {
        Assert.True(Coordinates.TryParse("45°30'00\"N 1°30'00\"O", out var french));
        Assert.True(Coordinates.TryParse("45°30'00\"N 1°30'00\"W", out var english));
        Assert.Equal(-1.5, french.Longitude, 1e-9);
        Assert.Equal(french, english);
    }

    [Theory]
    [InlineData(CoordinateFormat.DecimalDegrees)]
    [InlineData(CoordinateFormat.DegreesMinutesSeconds)]
    [InlineData(CoordinateFormat.Utm)]
    public void Formatted_text_can_be_parsed_back(CoordinateFormat format)
    {
        var point = new GeoPoint(45.188529, 5.724524);

        Assert.True(Coordinates.TryParse(Coordinates.Format(point, format), out var parsed));
        Assert.Equal(point.Latitude, parsed.Latitude, 1e-4);
        Assert.Equal(point.Longitude, parsed.Longitude, 1e-4);
    }
}
