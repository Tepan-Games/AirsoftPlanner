using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Gps;

/// <summary>Messages reçus pendant une même mission (ou hors mission), du plus ancien au plus récent.</summary>
public record MessageGroup(string Mission, IReadOnlyList<PhoneMessage> Messages);

/// <summary>Historique des messages d'une équipe : mission en cours au moment de chaque message, regroupement.</summary>
public static class MessageHistory
{
    /// <summary>
    /// Mission diffusée à l'équipe au moment de chaque message, déduite des annonces de mission
    /// (une annonce ouvre la mission, une fin la clôt ; la fin appartient encore à la mission).
    /// </summary>
    /// <param name="messages">Messages destinés à l'équipe.</param>
    public static IReadOnlyList<(OrgaMessage Message, string Mission)> TagMissions(IEnumerable<OrgaMessage> messages, Func<Guid, string> missionName)
    {
        var result = new List<(OrgaMessage, string)>();
        var current = "";
        foreach (var message in messages.OrderBy(m => m.SentAt))
        {
            if (message.Kind == MessageKind.MissionAssigned && message.MissionId is { } assigned)
                current = missionName(assigned);
            result.Add((message, current));
            if (message.Kind == MessageKind.MissionEnded)
                current = "";
        }

        return result;
    }

    /// <summary>Regroupe les messages consécutifs d'une même mission, groupes du plus récent au plus ancien.</summary>
    public static IReadOnlyList<MessageGroup> Group(IEnumerable<PhoneMessage> messages)
    {
        var groups = new List<MessageGroup>();
        List<PhoneMessage>? run = null;
        var mission = "";
        foreach (var message in messages.OrderBy(m => m.SentAt))
        {
            if (run is null || message.Mission != mission)
            {
                run = [];
                mission = message.Mission;
                groups.Add(new MessageGroup(mission, run));
            }

            run.Add(message);
        }

        groups.Reverse();
        return groups;
    }

    /// <summary>Ajoute les nouveaux messages à l'historique conservé sur le téléphone (sans doublon, taille limitée).</summary>
    public static IReadOnlyList<PhoneMessage> Merge(IEnumerable<PhoneMessage> archive, IEnumerable<PhoneMessage> received, int max = 500) =>
        archive.Concat(received)
            .GroupBy(m => m.Id)
            .Select(g => g.Last())
            .OrderBy(m => m.SentAt)
            .TakeLast(max)
            .ToList();
}
