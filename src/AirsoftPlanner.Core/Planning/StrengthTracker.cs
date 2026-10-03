using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

/// <param name="MemberId">Joueur hors jeu, ou null pour des joueurs non nommés.</param>
/// <param name="Players">Nombre de joueurs concernés.</param>
/// <param name="Reason">Raison de la sortie.</param>
/// <param name="Since">Heure de la sortie.</param>
/// <param name="Notes">Précisions.</param>
public record OutPlayer(Guid? MemberId, int Players, OutReason Reason, DateTimeOffset Since, string Notes);

/// <param name="Present">Joueurs en jeu.</param>
/// <param name="Out">Joueurs hors jeu à cet instant.</param>
public record TeamStrength(int Present, IReadOnlyList<OutPlayer> Out)
{
    public int OutCount => Out.Sum(o => o.Players);
}

/// <summary>Effectif réellement présent d'une équipe, à un instant ou tout au long de l'OP.</summary>
public static class StrengthTracker
{
    /// <param name="teamSize">Effectif de l'équipe au départ.</param>
    /// <param name="events">Sorties et retours de l'équipe.</param>
    /// <param name="at">Instant considéré (les événements postérieurs sont ignorés).</param>
    public static TeamStrength StrengthAt(int teamSize, IEnumerable<PlayerStatusEvent> events, DateTimeOffset at)
    {
        var out_ = new List<OutPlayer>();
        foreach (var e in events.Where(e => e.At <= at).OrderBy(e => e.At))
        {
            if (e.MemberId is { } member)
            {
                out_.RemoveAll(o => o.MemberId == member);
                if (e.IsOut)
                    out_.Add(new OutPlayer(member, 1, e.Reason, e.At, e.Notes));
            }
            else if (e.IsOut)
            {
                out_.Add(new OutPlayer(null, Math.Max(1, e.Players), e.Reason, e.At, e.Notes));
            }
            else
            {
                // Retour de joueurs non nommés : on fait revenir les sorties les plus anciennes d'abord.
                var back = Math.Max(1, e.Players);
                foreach (var anonymous in out_.Where(o => o.MemberId is null).OrderBy(o => o.Since).ToList())
                {
                    if (back == 0)
                        break;
                    var returning = Math.Min(back, anonymous.Players);
                    out_.Remove(anonymous);
                    if (anonymous.Players > returning)
                        out_.Add(anonymous with { Players = anonymous.Players - returning });
                    back -= returning;
                }
            }
        }

        var outCount = out_.Sum(o => o.Players);
        return new TeamStrength(Math.Max(0, teamSize - outCount), out_.OrderBy(o => o.Since).ToList());
    }

    /// <summary>Évolution de l'effectif présent : un point à chaque sortie ou retour.</summary>
    public static IReadOnlyList<(DateTimeOffset At, int Present)> Timeline(int teamSize, IReadOnlyCollection<PlayerStatusEvent> events) =>
        events.Select(e => e.At).Distinct().Order()
            .Select(at => (at, StrengthAt(teamSize, events, at).Present))
            .ToList();
}
