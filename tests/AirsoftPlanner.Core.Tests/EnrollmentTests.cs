using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

public class EnrollmentTests
{
    [Fact]
    public void Generated_codes_are_short_unambiguous_and_unique()
    {
        var codes = new HashSet<string>();
        for (var i = 0; i < 500; i++)
            codes.Add(EnrollmentCodes.Generate(codes));

        Assert.Equal(500, codes.Count);
        Assert.All(codes, c =>
        {
            Assert.Equal(EnrollmentCodes.Length, c.Length);
            Assert.DoesNotContain(c, ch => "01ILO".Contains(ch));
        });
    }

    [Fact]
    public void Typed_codes_are_normalized_and_displayed_in_groups()
    {
        Assert.Equal("K7P4QZ", EnrollmentCodes.Normalize(" k7p-4qz "));
        Assert.Equal("K7P-4QZ", EnrollmentCodes.Format("K7P4QZ"));
    }

    [Fact]
    public void Enrollment_link_round_trips()
    {
        var link = EnrollmentLink.Create("http://192.168.1.20:5055", "K7P4QZ");

        Assert.StartsWith("airsoftplanner://enroll?", link);
        Assert.True(EnrollmentLink.TryParse(link, out var server, out var code));
        Assert.Equal(("http://192.168.1.20:5055", "K7P4QZ"), (server, code));
        Assert.False(EnrollmentLink.TryParse("https://example.com/?code=1", out _, out _));
    }

    [Fact]
    public void Tokens_are_random()
    {
        Assert.NotEqual(EnrollmentCodes.NewToken(), EnrollmentCodes.NewToken());
        Assert.Equal(32, EnrollmentCodes.NewToken().Length);
    }
}
