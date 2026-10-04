using System.Net.Http.Headers;
using System.Text.Json;

namespace AirsoftPlanner.Core.Updates;

/// <summary>Version publiée sur GitHub (dernière « release »).</summary>
/// <param name="ArchiveUrl">Archive complète (logiciel Windows avec installation, application, guide).</param>
/// <param name="ApkUrl">Application Android.</param>
public record ReleaseInfo(Version Version, string Tag, string Name, string PageUrl, string? ArchiveUrl, string? ApkUrl, string Notes);

/// <summary>
/// Recherche d'une nouvelle version sur le dépôt GitHub du projet (API publique des « releases »).
/// Dépôt privé, absence de réseau ou de version publiée : aucune information, sans erreur.
/// </summary>
public static class UpdateChecker
{
    public const string Repository = "Tepan-Games/AirsoftPlanner";

    public const string ProjectUrl = "https://github.com/" + Repository;

    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>« v1.2.3 », « 1.2 », « v1.0.0-beta.2 » → 1.2.3, 1.2.0, 1.0.0 ; null si illisible.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['-', '+', ' ']);
        if (end >= 0)
            text = text[..end];
        if (!Version.TryParse(text.Contains('.') ? text : text + ".0", out var version))
            return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    /// <summary>Lit la réponse de l'API GitHub (dernière release).</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                return null;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (ParseVersion(tag) is not { } version)
                return null;

            string? archive = null, apk = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                        apk ??= url;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                        archive ??= url;
                }

            return new ReleaseInfo(version, tag!,
                root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } title ? title : tag!,
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? ProjectUrl : ProjectUrl,
                archive, apk,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static bool IsNewer(ReleaseInfo release, Version current) =>
        release.Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <returns>La dernière version publiée, ou null (dépôt privé, hors ligne, aucune version).</returns>
    public static async Task<ReleaseInfo?> GetLatestAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AirsoftPlanner", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode ? Parse(await response.Content.ReadAsStringAsync(cancellationToken)) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }
}
