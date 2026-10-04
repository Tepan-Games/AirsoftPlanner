using System.Text.RegularExpressions;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Documents;

/// <summary>Règlement de jeu de l'OP : règles propres (documents rédigés ou importés) ou règlement ACP.</summary>
/// <remarks>Stocké en texte ; Custom vient en premier (OP créées avant cette option).</remarks>
public enum GameRuleSet
{
    Custom,
    Acp,
}

/// <summary>
/// Règlement ACP (https://www.acp-rules.org) : le PDF officiel est téléchargé depuis le site et joint tel quel aux
/// documents de l'OP (licence CC BY-NC-ND 4.0 : pas de modification, pas d'usage commercial, mention de l'auteur).
/// </summary>
public static partial class AcpRules
{
    public const string HomeUrl = "https://www.acp-rules.org/";

    /// <summary>Origine des documents de règles ajoutés automatiquement (<see cref="RuleDocument.Origin"/>).</summary>
    public const string Origin = "acp";

    public static string Label(GameRuleSet set) => set == GameRuleSet.Acp ? L.T("reglement_acp") : L.T("regles_propres_a_l_op");

    /// <summary>Lien du règlement complet (PDF) sur la page d'accueil du site, ou null s'il n'y figure pas.</summary>
    public static string? FindPdfUrl(string html)
    {
        var links = PdfLink().Matches(html).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups["url"].Value)).Distinct().ToList();
        // Le règlement, pas les cartes mémo.
        var url = links.FirstOrDefault(l => l.Contains("reglement", StringComparison.OrdinalIgnoreCase)
                                            && !l.Contains("memo", StringComparison.OrdinalIgnoreCase)
                                            && !l.Contains("carte", StringComparison.OrdinalIgnoreCase));
        if (url is null)
            return null;
        return Uri.TryCreate(new Uri(HomeUrl), url, out var absolute) ? absolute.ToString() : null;
    }

    /// <summary>« …-v5.1.0-EwbKC2….pdf » → « 5.1.0 » ; vide si le nom ne porte pas de version.</summary>
    public static string VersionOf(string fileNameOrUrl)
    {
        var match = Version().Match(fileNameOrUrl);
        return match.Success ? match.Groups["v"].Value : "";
    }

    /// <summary>Nom de fichier lisible, sans le suffixe aléatoire de l'hébergeur.</summary>
    public static string FileName(string version) => version.Length > 0 ? $"Reglement-ACP-v{version}.pdf" : "Reglement-ACP.pdf";

    public static string Title(string version) => version.Length > 0 ? L.F("reglement_acp_x", version) : L.T("reglement_acp");

    [GeneratedRegex("""href\s*=\s*["'](?<url>[^"']+?\.pdf)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex PdfLink();

    [GeneratedRegex(@"v(?<v>\d+(?:\.\d+){1,2})", RegexOptions.IgnoreCase)]
    private static partial Regex Version();
}
