using System.Globalization;
using System.Text.RegularExpressions;

namespace AirsoftPlanner.Core.Planning;

/// <summary>Heures du jour de l'OP, en minutes depuis minuit (au-delà de 24 h : jour suivant).</summary>
public static partial class MissionTime
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>« 09:30 », ou « J+1 02:00 » pour le lendemain.</summary>
    public static string Format(int minutes)
    {
        var day = (int)Math.Floor(minutes / (double)MinutesPerDay);
        var inDay = minutes - day * MinutesPerDay;
        var time = string.Create(CultureInfo.InvariantCulture, $"{inDay / 60:00}:{inDay % 60:00}");
        return day == 0 ? time : $"J{(day > 0 ? "+" : "")}{day} {time}";
    }

    /// <summary>« 1 h 30 », « 45 min ».</summary>
    public static string FormatDuration(int minutes) => minutes switch
    {
        < 60 => $"{minutes} min",
        _ when minutes % 60 == 0 => $"{minutes / 60} h",
        _ => $"{minutes / 60} h {minutes % 60:00}",
    };

    /// <summary>Lit « 9:30 », « 09h30 », « 9h », « 930 », « J+1 02:00 ».</summary>
    public static bool TryParse(string? text, out int minutes)
    {
        minutes = 0;
        var match = TimePattern().Match(text ?? "");
        if (!match.Success)
            return false;

        var day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture) : 0;
        var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var mins = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (hours > 23 || mins > 59)
            return false;

        minutes = day * MinutesPerDay + hours * 60 + mins;
        return true;
    }

    [GeneratedRegex(@"^\s*(?:[Jj]\s*(?<day>[+-]?\d+)\s+)?(?<h>\d{1,2})(?:\s*[:hH]\s*(?<m>\d{2})?|(?<m>\d{2}))?\s*$")]
    private static partial Regex TimePattern();
}
