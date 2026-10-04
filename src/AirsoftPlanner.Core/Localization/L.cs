using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirsoftPlanner.Core.Localization;

/// <summary>Langue proposée dans le logiciel et l'application.</summary>
public record Language(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Traductions du logiciel, des documents et de l'application Android. Les textes sont rangés par clé dans
/// des fichiers JSON (un par langue, embarqués) ; le français est la langue de référence : une clé absente
/// d'une autre langue s'affiche en français.
/// </summary>
public static class L
{
    public static IReadOnlyList<Language> Languages { get; } =
    [
        new("fr", "Français"),
        new("en", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("it", "Italiano"),
    ];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Tables = [];
    private static IReadOnlyDictionary<string, string> _current = Table("fr");
    private static readonly IReadOnlyDictionary<string, string> Reference = Table("fr");

    /// <summary>Code de la langue en cours (« fr », « en »...).</summary>
    public static string Code { get; private set; } = "fr";

    /// <summary>Culture correspondante (dates, nombres).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Change la langue (code inconnu : français).</summary>
    public static void SetLanguage(string? code)
    {
        Code = Languages.Any(l => l.Code == code) ? code! : "fr";
        _current = Table(Code);
        Culture = CultureInfo.GetCultureInfo(Code switch
        {
            "en" => "en-GB",
            "de" => "de-DE",
            "es" => "es-ES",
            "it" => "it-IT",
            _ => "fr-FR",
        });
    }

    /// <summary>Langue du système si elle est proposée, sinon français.</summary>
    public static string SystemLanguage() =>
        Languages.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)?.Code ?? "fr";

    /// <summary>Texte traduit.</summary>
    public static string T(string key) =>
        _current.TryGetValue(key, out var text) || Reference.TryGetValue(key, out text) ? text : key;

    /// <summary>Texte traduit avec des valeurs ({0}, {1}...), mises en forme selon la langue.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Toutes les clés de la langue de référence (vérification des traductions).</summary>
    public static IReadOnlyCollection<string> Keys => Reference.Keys.ToList();

    /// <summary>Clés traduites dans une langue donnée.</summary>
    public static IReadOnlyCollection<string> KeysOf(string code) => Table(code).Keys.ToList();

    private static IReadOnlyDictionary<string, string> Table(string code)
    {
        lock (Tables)
        {
            if (Tables.TryGetValue(code, out var table))
                return table;

            var assembly = typeof(L).Assembly;
            using var stream = assembly.GetManifestResourceStream($"AirsoftPlanner.Core.Localization.{code}.json");
            table = stream is null
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize(stream, LocalizationJson.Default.DictionaryStringString) ?? [];
            Tables[code] = table;
            return table;
        }
    }
}

/// <summary>Lecture des fichiers de traduction générée à la compilation (application Android élaguée).</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class LocalizationJson : JsonSerializerContext;
