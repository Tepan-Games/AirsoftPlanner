namespace AirsoftPlanner.Core.Gps;

/// <summary>
/// Mise à jour d'un nom DynDNS (DuckDNS, No-IP, Dynu...) avec l'adresse actuelle du PC de l'OP :
/// les téléphones enrôlés avec ce nom suivent le PC même si son IP change.
/// </summary>
public static class DynDns
{
    /// <summary>Repère remplacé par l'adresse IP dans l'adresse de mise à jour.</summary>
    public const string IpPlaceholder = "{ip}";

    /// <summary>
    /// Adresse de mise à jour pour une IP donnée. Sans repère <c>{ip}</c>, le service retient l'adresse
    /// d'où vient la demande (IP publique de la box : nécessite une redirection de port).
    /// </summary>
    public static string BuildUrl(string template, string ip) =>
        template.Trim().Replace(IpPlaceholder, Uri.EscapeDataString(ip), StringComparison.OrdinalIgnoreCase);

    /// <summary>Interprète la réponse des services courants (« OK » DuckDNS, « good »/« nochg » No-IP et DynDNS).</summary>
    public static bool IsSuccess(string response)
    {
        var text = response.Trim().ToLowerInvariant();
        return text.StartsWith("ok") || text.StartsWith("good") || text.StartsWith("nochg");
    }
}
