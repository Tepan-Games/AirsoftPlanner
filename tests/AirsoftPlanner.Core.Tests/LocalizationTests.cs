using System.Text.RegularExpressions;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Tests;

[Collection("Langue")]
public class LocalizationTests
{
    public static TheoryData<string> OtherLanguages => new() { "en", "de", "es", "it" };

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Every_text_is_translated(string code)
    {
        var missing = L.Keys.Except(L.KeysOf(code)).ToList();
        Assert.True(missing.Count == 0, $"{code} : {missing.Count} texte(s) sans traduction, ex. {string.Join(", ", missing.Take(5))}");
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Translations_keep_the_values_of_the_french_text(string code)
    {
        var placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
        foreach (var key in L.Keys)
        {
            var french = placeholder.Matches(L.TIn("fr", key)).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            var text = L.TIn(code, key);
            var translated = placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            Assert.True(french.SequenceEqual(translated), $"{code} « {key} » : valeurs {string.Join(",", translated)} au lieu de {string.Join(",", french)}");
            if (french.Count > 0)
                _ = string.Format(text, Enumerable.Range(0, 10).Select(_ => (object)new AnyValue()).ToArray()); // format valide
        }
    }

    /// <summary>Valeur qui accepte tous les formats (vérification de la syntaxe des textes).</summary>
    private sealed class AnyValue : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => "x";
    }

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        try
        {
            L.SetLanguage("xx");
            Assert.Equal("en", L.Code);
            Assert.Equal("Teams", L.T("equipes"));
            L.SetLanguage("fr");
            Assert.Equal("Équipes", L.T("equipes"));
            Assert.Equal("cle_inconnue", L.T("cle_inconnue"));
        }
        finally
        {
            L.SetLanguage("fr");
        }
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("it-CH", "it")]
    [InlineData("pt-BR", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("fr-CA", "fr")]
    public void System_language_is_used_when_available_otherwise_english(string culture, string expected) =>
        Assert.Equal(expected, L.SystemLanguage(System.Globalization.CultureInfo.GetCultureInfo(culture)));
}

/// <summary>Tests qui changent la langue du programme : jamais en parallèle des autres.</summary>
[CollectionDefinition("Langue", DisableParallelization = true)]
public class LanguageCollection;
