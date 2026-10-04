using System.Security.Cryptography;
using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Gps;

/// <summary>Téléphone enrôlé auprès du PC de l'OP avec le code d'une équipe (ou d'un orga).</summary>
public class EnrolledDevice : Entity
{
    /// <summary>Équipe du téléphone, ou orga quand <see cref="IsOrganizer"/> est vrai.</summary>
    public Guid TeamId { get; set; }

    /// <summary>Téléphone d'un orga (arbitre, PC) : sa position est suivie, il voit toutes les équipes.</summary>
    public bool IsOrganizer { get; set; }

    /// <summary>Jeton secret remis au téléphone à l'enrôlement, présenté à chaque envoi de position.</summary>
    public string Token { get; set; } = "";

    /// <summary>Nom donné par le téléphone (« Pixel de Julien »).</summary>
    public string DeviceName { get; set; } = "";

    public DateTimeOffset EnrolledAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public bool IsRevoked { get; set; }
}

/// <summary>Codes d'enrôlement des équipes : courts, faciles à dicter à la radio ou à recopier.</summary>
public static class EnrollmentCodes
{
    /// <summary>Sans caractères ambigus (0/O, 1/I/L).</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    /// <summary>Nouveau code, différent de ceux déjà utilisés dans l'OP.</summary>
    public static string Generate(IReadOnlyCollection<string> existing)
    {
        while (true)
        {
            var code = new string(Enumerable.Range(0, Length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            if (!existing.Contains(code))
                return code;
        }
    }

    /// <summary>Code saisi par l'utilisateur, normalisé : majuscules, sans espaces ni tirets (« k7p-4qz » → « K7P4QZ »).</summary>
    public static string Normalize(string input) => new(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Affichage par groupes de trois : « K7P-4QZ ».</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..3]}-{code[3..]}" : code;

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
