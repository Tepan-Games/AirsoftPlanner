using System.Net.Http.Headers;
using System.Text.Json;

namespace AirsoftPlanner.Core.Updates;

/// <summary>Adresses du formulaire d'avis et de la page de dons, lues dans <c>liens.json</c> à la racine du dépôt.</summary>
public record ProjectLinkSet(string Feedback, string Donate);

/// <summary>
/// Liens « Donner mon avis » et « Soutenir le projet » : lus au moment du clic dans le fichier <c>liens.json</c> du dépôt
/// GitHub, pour pouvoir les changer sans publier de nouvelle version. Sans réseau ou sans lien renseigné : pages GitHub
/// du projet (modèles de retour, section « Soutenir » du README).
/// </summary>
public static class ProjectLinks
{
    public const string LinksFileUrl = "https://raw.githubusercontent.com/" + UpdateChecker.Repository + "/main/liens.json";

    public const string FeedbackFallback = UpdateChecker.ProjectUrl + "/issues/new/choose";

    public const string DonateFallback = UpdateChecker.ProjectUrl + "#soutenir-le-projet";

    /// <summary>Lit <c>liens.json</c> (<c>{"avis": "...", "dons": "..."}</c>) ; liens vides ou illisibles : pages GitHub.</summary>
    public static ProjectLinkSet Parse(string? json)
    {
        string? feedback = null, donate = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                feedback = Web(document.RootElement, "avis");
                donate = Web(document.RootElement, "dons");
            }
            catch (JsonException)
            {
                // Fichier mal formé : liens de secours.
            }
        }
        return new ProjectLinkSet(feedback ?? FeedbackFallback, donate ?? DonateFallback);
    }

    public static async Task<ProjectLinkSet> GetAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LinksFileUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AirsoftPlanner", "1.0"));
            using var response = await http.SendAsync(request, cancellationToken);
            return Parse(response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return Parse(null);
        }
    }

    /// <summary>
    /// Formulaire d'avis avec la version, la plateforme et la langue (champs cachés d'un formulaire Tally ou Google Forms ;
    /// ignorés par les autres pages).
    /// </summary>
    public static string WithContext(string url, string version, string platform, string language)
    {
        if (url == FeedbackFallback)
            return url;
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}version={Uri.EscapeDataString(version)}&plateforme={Uri.EscapeDataString(platform)}&langue={Uri.EscapeDataString(language)}";
    }

    private static string? Web(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.ToString()
            : null;
}
