using AirsoftPlanner.Core.Documents;

namespace AirsoftPlanner.Core.Tests;

public class AcpRulesTests
{
    // Extrait de la page d'accueil d'acp-rules.org (octobre 2026).
    private const string Home = """
        <a href="https://www.acp-rules.org/reglement-acp-version-51">Version 5.1</a>
        <a href="https://assets.zyrosite.com/m5KLeGJOgbFKrVLb/cartes-memo-acp-v4.0-A4.pdf">Cartes mémo</a>
        <a class="button" href="https://assets.zyrosite.com/m5KLeGJOgbFKrVLb/reglement-advanced-concept-partner-v5.1.0-EwbKC2LfdHQIcRVX.pdf">Télécharger le Règlement V.5.1.0</a>
        """;

    [Fact]
    public void Rules_pdf_link_is_found_on_the_home_page_but_not_the_memo_cards()
    {
        var url = AcpRules.FindPdfUrl(Home);
        Assert.Equal("https://assets.zyrosite.com/m5KLeGJOgbFKrVLb/reglement-advanced-concept-partner-v5.1.0-EwbKC2LfdHQIcRVX.pdf", url);
        Assert.Equal("5.1.0", AcpRules.VersionOf(url!));
        Assert.Equal("Reglement-ACP-v5.1.0.pdf", AcpRules.FileName("5.1.0"));
    }

    [Fact]
    public void Relative_links_are_made_absolute_and_missing_links_give_nothing()
    {
        Assert.Equal("https://www.acp-rules.org/docs/reglement-acp-v6.pdf", AcpRules.FindPdfUrl("""<a href='/docs/reglement-acp-v6.pdf'>"""));
        Assert.Null(AcpRules.FindPdfUrl("<p>Site en maintenance</p>"));
        Assert.Equal("", AcpRules.VersionOf("reglement.pdf"));
    }
}
