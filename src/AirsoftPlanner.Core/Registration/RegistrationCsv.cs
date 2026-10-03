using System.Globalization;
using System.Text;

namespace AirsoftPlanner.Core.Registration;

/// <summary>Une inscription lue dans un fichier CSV (export de formulaire en ligne ou de tableur).</summary>
public record RegistrationRow(
    string TeamName,
    string Faction,
    string ContactName,
    string Phone,
    string Email,
    int Players,
    string Notes);

/// <summary>
/// Lecture et écriture des inscriptions en CSV. Les colonnes sont reconnues par leur intitulé
/// (« Équipe », « Faction », « Contact », « Téléphone », « Mail », « Joueurs »...), dans n'importe quel ordre ;
/// le séparateur (point-virgule, virgule ou tabulation) est détecté.
/// </summary>
public static class RegistrationCsv
{
    private static readonly Dictionary<string, string[]> HeaderKeywords = new()
    {
        ["team"] = ["équipe", "equipe", "team", "groupe", "nom de l'équipe", "nom d'équipe"],
        ["faction"] = ["faction", "camp", "side"],
        ["contact"] = ["contact", "chef", "responsable", "référent", "referent", "leader", "nom"],
        ["phone"] = ["téléphone", "telephone", "tél", "tel", "portable", "mobile", "phone"],
        ["email"] = ["mail", "e-mail", "email", "courriel"],
        ["players"] = ["joueurs", "effectif", "nombre", "nb", "players", "participants"],
        ["notes"] = ["remarque", "commentaire", "notes", "note", "message"],
    };

    public static IReadOnlyList<RegistrationRow> Parse(string text)
    {
        var lines = SplitRecords(text.TrimStart('﻿')).Where(l => l.Any(f => f.Trim().Length > 0)).ToList();
        if (lines.Count < 2)
            return [];

        var columns = MapHeaders(lines[0]);
        if (!columns.ContainsKey("team"))
            throw new FormatException("Colonne « Équipe » introuvable dans l'en-tête du fichier.");

        string Field(IReadOnlyList<string> row, string key) =>
            columns.TryGetValue(key, out var index) && index < row.Count ? row[index].Trim() : "";

        return lines.Skip(1)
            .Select(row => new RegistrationRow(
                Field(row, "team"),
                Field(row, "faction"),
                Field(row, "contact"),
                Field(row, "phone"),
                Field(row, "email"),
                int.TryParse(new string(Field(row, "players").Where(char.IsDigit).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var players) ? players : 0,
                Field(row, "notes")))
            .Where(r => r.TeamName.Length > 0)
            .ToList();
    }

    public static string Write(IEnumerable<(RegistrationRow Row, string Status, DateTimeOffset? RegisteredAt)> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(Line(["Équipe", "Faction", "Statut", "Inscrite le", "Contact", "Téléphone", "Mail", "Joueurs", "Remarques"]));
        foreach (var (row, status, registeredAt) in rows)
            text.AppendLine(Line([row.TeamName, row.Faction, status, registeredAt?.LocalDateTime.ToString("dd/MM/yyyy HH:mm") ?? "",
                row.ContactName, row.Phone, row.Email, row.Players.ToString(CultureInfo.InvariantCulture), row.Notes]));
        return text.ToString();

        // Point-virgule : séparateur attendu par Excel en français.
        static string Line(IEnumerable<string> fields) => string.Join(";", fields.Select(Quote));
        static string Quote(string value) => value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static Dictionary<string, int> MapHeaders(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>();
        // Les mots-clés les plus précis d'abord : « nom de l'équipe » ne doit pas être pris pour le contact.
        foreach (var key in new[] { "team", "faction", "phone", "email", "players", "notes", "contact" })
        {
            for (var i = 0; i < header.Count; i++)
            {
                var title = header[i].Trim().ToLowerInvariant();
                if (map.ContainsValue(i) || !HeaderKeywords[key].Any(title.Contains))
                    continue;
                map[key] = i;
                break;
            }
        }

        return map;
    }

    /// <summary>Découpe le texte en enregistrements CSV (guillemets, retours à la ligne dans les champs).</summary>
    private static IEnumerable<IReadOnlyList<string>> SplitRecords(string text)
    {
        var firstLine = text.Split('\n')[0];
        var separator = new[] { ';', '\t', ',' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return fields;
                fields = [];
            }
            else
                field.Append(c);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString().TrimEnd('\r'));
            yield return fields;
        }
    }
}
