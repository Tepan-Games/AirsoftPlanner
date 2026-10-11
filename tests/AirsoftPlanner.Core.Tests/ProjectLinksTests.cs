using AirsoftPlanner.Core.Updates;

namespace AirsoftPlanner.Core.Tests;

public class ProjectLinksTests
{
    [Fact]
    public void Links_come_from_the_repository_file()
    {
        var links = ProjectLinks.Parse("""{ "avis": "https://tally.so/r/abc123", "dons": "https://ko-fi.com/tepangames" }""");
        Assert.Equal(("https://tally.so/r/abc123", "https://ko-fi.com/tepangames"), (links.Feedback, links.Donate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("""{ "avis": "", "dons": "" }""")]
    [InlineData("""{ "avis": "http://exemple.fr/non-chiffre", "dons": "javascript:alert(1)" }""")]
    public void Missing_or_unsafe_links_fall_back_to_github(string? json)
    {
        var links = ProjectLinks.Parse(json);
        Assert.Equal((ProjectLinks.FeedbackFallback, ProjectLinks.DonateFallback), (links.Feedback, links.Donate));
    }

    [Fact]
    public void Feedback_form_receives_version_platform_and_language()
    {
        Assert.Equal("https://tally.so/r/abc123?version=0.9.3&plateforme=android&langue=fr",
            ProjectLinks.WithContext("https://tally.so/r/abc123", "0.9.3", "android", "fr"));
        Assert.Equal("https://docs.google.com/forms/d/e/x/viewform?usp=sf_link&version=0.9.3&plateforme=windows&langue=de",
            ProjectLinks.WithContext("https://docs.google.com/forms/d/e/x/viewform?usp=sf_link", "0.9.3", "windows", "de"));
        Assert.Equal(ProjectLinks.FeedbackFallback, ProjectLinks.WithContext(ProjectLinks.FeedbackFallback, "0.9.3", "windows", "fr"));
    }
}
