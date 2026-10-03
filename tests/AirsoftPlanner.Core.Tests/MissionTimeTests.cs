using AirsoftPlanner.Core.Planning;

namespace AirsoftPlanner.Core.Tests;

public class MissionTimeTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(570, "09:30")]
    [InlineData(1439, "23:59")]
    [InlineData(1440 + 120, "J+1 02:00")]
    public void Format(int minutes, string expected) => Assert.Equal(expected, MissionTime.Format(minutes));

    [Theory]
    [InlineData("9:30", 570)]
    [InlineData("09:30", 570)]
    [InlineData("9h30", 570)]
    [InlineData("9H", 540)]
    [InlineData("9", 540)]
    [InlineData("930", 570)]
    [InlineData(" 14 h 05 ", 845)]
    [InlineData("J+1 02:00", 1560)]
    public void TryParse_accepts_common_formats(string text, int expected)
    {
        Assert.True(MissionTime.TryParse(text, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("10:75")]
    [InlineData("midi")]
    public void TryParse_rejects_invalid_times(string text) => Assert.False(MissionTime.TryParse(text, out _));

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30")]
    public void FormatDuration(int minutes, string expected) => Assert.Equal(expected, MissionTime.FormatDuration(minutes));
}
