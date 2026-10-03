using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Documents;

/// <summary>
/// Empreinte de ce que contient le package d'une équipe. Si elle change après l'envoi (règles modifiées,
/// mission déplacée, fréquence changée...), le package doit être renvoyé.
/// </summary>
public static class PackageFingerprint
{
    public static string Compute(Team team, Faction? faction, IEnumerable<Mission> teamMissions,
        IReadOnlyDictionary<Guid, Zone> zones, IEnumerable<RuleDocument> rules)
    {
        var text = new StringBuilder();
        void Line(params object?[] values) =>
            text.AppendLine(string.Join("|", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))));

        Line("team", team.Name, team.RadioFrequency);
        Line("faction", faction?.Name, faction?.RadioFrequency, faction?.Uniform, faction?.ArmbandColor, faction?.CommandTeamId);
        foreach (var mission in teamMissions.Where(m => m.IsEnabled).OrderBy(m => m.StartMinutes).ThenBy(m => m.Id))
        {
            Line("mission", mission.Id, mission.Name, mission.Description, mission.StartMinutes, mission.DurationMinutes,
                string.Join(",", mission.Items.Select(i => $"{i.ItemId}:{i.Quantity}")));
            if (mission.ZoneId is { } zoneId && zones.TryGetValue(zoneId, out var zone))
                Line("zone", zone.Name, zone.Description, string.Join(";", zone.Points.Select(p => p.ToString())));
        }

        foreach (var rule in rules.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            Line("rule", rule.Title, rule.FileName, rule.IsImported ? Convert.ToHexString(SHA256.HashData(rule.FileContent)) : rule.Text);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}

public enum PackageStatus
{
    /// <summary>Jamais généré.</summary>
    NotGenerated,

    /// <summary>Généré, pas encore envoyé.</summary>
    ReadyToSend,

    /// <summary>Envoyé, réception non confirmée.</summary>
    AwaitingConfirmation,

    /// <summary>Réception confirmée et contenu à jour.</summary>
    Received,

    /// <summary>Le contenu a changé depuis la génération : à régénérer et renvoyer.</summary>
    Outdated,
}

public static class PackageState
{
    public static PackageStatus Of(TeamPackage? package, string currentFingerprint) => package switch
    {
        null or { GeneratedAt: null } => PackageStatus.NotGenerated,
        _ when package.Fingerprint != currentFingerprint => PackageStatus.Outdated,
        { ReceivedAt: not null } => PackageStatus.Received,
        { SentAt: not null } => PackageStatus.AwaitingConfirmation,
        _ => PackageStatus.ReadyToSend,
    };
}
