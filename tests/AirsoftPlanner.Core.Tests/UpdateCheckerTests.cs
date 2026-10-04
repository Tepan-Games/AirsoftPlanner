using AirsoftPlanner.Core.Updates;

namespace AirsoftPlanner.Core.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v1.0.0-beta.2", "1.0.0")]
    [InlineData("V2", "2.0.0")]
    public void Release_tags_are_read(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    public void Unreadable_tags_are_ignored(string tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void Github_release_is_parsed_with_its_assets()
    {
        const string json = """
            {
              "tag_name": "v1.1.0", "name": "Airsoft Planner 1.1", "draft": false,
              "html_url": "https://github.com/Tepan-Games/AirsoftPlanner/releases/tag/v1.1.0",
              "body": "Nouveautés",
              "assets": [
                { "name": "AirsoftPlanner-1.1.0-win-x64.zip", "browser_download_url": "https://example.org/win.zip" },
                { "name": "AirsoftPlanner-1.1.0.zip", "browser_download_url": "https://example.org/full.zip" },
                { "name": "AirsoftPlanner-1.1.0-Setup.exe", "browser_download_url": "https://example.org/setup.exe" },
                { "name": "AirsoftPlanner.apk", "browser_download_url": "https://example.org/app.apk" }
              ]
            }
            """;
        var release = UpdateChecker.Parse(json)!;
        Assert.Equal(new Version(1, 1, 0), release.Version);
        Assert.Equal("https://example.org/setup.exe", release.SetupUrl);
        Assert.Equal("https://example.org/app.apk", release.ApkUrl);
        Assert.True(UpdateChecker.IsNewer(release, new Version(0, 9, 0, 0)));
        Assert.False(UpdateChecker.IsNewer(release, new Version(1, 1, 0)));
    }

    [Fact]
    public void Drafts_and_errors_give_no_release()
    {
        Assert.Null(UpdateChecker.Parse("""{ "tag_name": "v2.0.0", "draft": true }"""));
        Assert.Null(UpdateChecker.Parse("""{ "message": "Not Found" }"""));
        Assert.Null(UpdateChecker.Parse("pas du json"));
    }
}
