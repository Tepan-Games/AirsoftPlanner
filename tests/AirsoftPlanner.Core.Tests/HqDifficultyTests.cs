using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Gps;

namespace AirsoftPlanner.Core.Tests;

public class HqDifficultyTests
{
    [Fact]
    public void Faction_level_overrides_the_operation_level()
    {
        var operation = new Operation { HqDifficulty = HqDifficulty.Medium };
        Assert.Equal(HqDifficulty.Medium, HqDifficultyRules.For(operation, new Faction()));
        Assert.Equal(HqDifficulty.Medium, HqDifficultyRules.For(operation, null));
        Assert.Equal(HqDifficulty.Extreme, HqDifficultyRules.For(operation, new Faction { HqDifficulty = HqDifficulty.Extreme }));
    }

    [Theory]
    [InlineData(HqDifficulty.Easy, true, true, AllyShareMode.Map)]
    [InlineData(HqDifficulty.Medium, true, true, AllyShareMode.Coordinates)]
    [InlineData(HqDifficulty.Hard, false, true, AllyShareMode.None)]
    [InlineData(HqDifficulty.Extreme, false, false, AllyShareMode.None)]
    public void Each_level_shares_less(HqDifficulty level, bool game, bool radio, AllyShareMode mode)
    {
        Assert.Equal(game, HqDifficultyRules.SharesGame(level));
        Assert.Equal(radio, HqDifficultyRules.SharesRadio(level));
        Assert.Equal(mode, HqDifficultyRules.ShareMode(level));
    }
}
