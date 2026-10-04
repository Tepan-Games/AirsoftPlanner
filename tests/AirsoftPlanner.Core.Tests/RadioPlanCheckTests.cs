using AirsoftPlanner.Core.Radio;

namespace AirsoftPlanner.Core.Tests;

public class RadioPlanCheckTests
{
    private static readonly RadioUser Alpha = new("team:a", "Alpha", "PMR 446 canal 3");
    private static readonly RadioUser Bravo = new("team:b", "Bravo", "pmr446 ch3");
    private static readonly RadioUser Charlie = new("team:c", "Charlie", "PMR 446 canal 4");

    [Theory]
    [InlineData("PMR 446 canal 8", "pmr446ch8")]
    [InlineData("pmr446 ch 8", "pmr446ch8")]
    [InlineData("446,00625 MHz", "446.00625")]
    [InlineData("446.00625", "446.00625")]
    [InlineData("  ", "")]
    public void Spellings_of_the_same_frequency_match(string input, string expected) =>
        Assert.Equal(expected, RadioPlanCheck.Normalize(input));

    [Fact]
    public void Two_teams_on_the_same_frequency_are_reported()
    {
        var conflict = Assert.Single(RadioPlanCheck.Find([Alpha, Bravo, Charlie]));
        Assert.Equal(["Alpha", "Bravo"], conflict.Users.Select(u => u.Label));
    }

    [Fact]
    public void Empty_frequencies_and_same_owner_are_not_conflicts()
    {
        Assert.Empty(RadioPlanCheck.Find([Alpha with { Frequency = "" }, Bravo with { Frequency = " " }]));
        // Deux orgas sur la fréquence de l'orga : même utilisateur « orga ».
        Assert.Empty(RadioPlanCheck.Find([new("orga", "Orga", "PMR 8"), new("orga", "Orga — Paul", "pmr 8")]));
    }

    [Fact]
    public void Ignored_conflict_comes_back_when_someone_else_joins()
    {
        var conflict = RadioPlanCheck.Find([Alpha, Bravo]).Single();
        Assert.Empty(RadioPlanCheck.Find([Alpha, Bravo, Charlie], [conflict.Signature]));

        var delta = new RadioUser("team:d", "Delta", "PMR 446 canal 3");
        var back = Assert.Single(RadioPlanCheck.Find([Alpha, Bravo, delta], [conflict.Signature]));
        Assert.Equal(3, back.Users.Count);
    }
}
