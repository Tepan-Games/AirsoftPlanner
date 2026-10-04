using System.Text;

namespace AirsoftPlanner.Core.Radio;

/// <summary>Utilisateur d'une fréquence : une équipe, une faction ou l'orga.</summary>
/// <param name="Key">Identifiant stable (ex. « team:guid ») : deux entrées de même clé ne sont pas en conflit.</param>
public record RadioUser(string Key, string Label, string Frequency);

/// <summary>Fréquence utilisée par plusieurs équipes, factions ou par l'orga.</summary>
public record RadioConflict(string Frequency, IReadOnlyList<RadioUser> Users, string Signature)
{
    public string Description => $"{Frequency} : {string.Join(", ", Users.Select(u => u.Label))}";
}

/// <summary>
/// Repère les fréquences radio saisies en double. Un doublon voulu (faction et équipe de commandement
/// sur la même fréquence...) peut être ignoré : il ne réapparaît que si quelqu'un d'autre rejoint la fréquence.
/// </summary>
public static class RadioPlanCheck
{
    /// <summary>« PMR 446 canal 8 », « pmr446 ch8 » ou « 446,00625 MHz » / « 446.00625 » : même fréquence.</summary>
    public static string Normalize(string frequency)
    {
        var text = frequency.Trim().ToLowerInvariant()
            .Replace(',', '.')
            .Replace("mhz", "")
            .Replace("channel", "ch")
            .Replace("canal", "ch")
            .Replace("cnl", "ch");
        var builder = new StringBuilder();
        foreach (var c in text)
            if (char.IsLetterOrDigit(c) || c == '.')
                builder.Append(c);
        return builder.ToString().Trim('.');
    }

    public static IReadOnlyList<RadioConflict> Find(IEnumerable<RadioUser> users, IEnumerable<string>? ignored = null)
    {
        var skip = (ignored ?? []).ToHashSet();
        return users
            .Select(u => (User: u, Normalized: Normalize(u.Frequency)))
            .Where(x => x.Normalized.Length > 0)
            .GroupBy(x => x.Normalized)
            .Select(g =>
            {
                var distinct = g.GroupBy(x => x.User.Key).Select(k => k.First().User).ToList();
                var signature = $"{g.Key}|{string.Join(",", distinct.Select(u => u.Key).Order(StringComparer.Ordinal))}";
                return new RadioConflict(g.First().User.Frequency.Trim(), distinct, signature);
            })
            .Where(c => c.Users.Count > 1 && !skip.Contains(c.Signature))
            .OrderBy(c => c.Frequency, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
