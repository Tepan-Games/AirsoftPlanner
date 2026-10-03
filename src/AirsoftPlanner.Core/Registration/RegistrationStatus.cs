namespace AirsoftPlanner.Core.Registration;

/// <remarks>Confirmed vient en premier : c'est la valeur des équipes des fichiers antérieurs à l'inscription.</remarks>
public enum RegistrationStatus
{
    Confirmed,

    /// <summary>Demande reçue, pas encore validée (paiement, assurance...).</summary>
    PreRegistered,

    /// <summary>Faction complète : l'équipe attend qu'une place se libère.</summary>
    WaitingList,

    Cancelled,
}

public static class RegistrationRules
{
    /// <summary>Équipes qui participent à l'OP : comptées dans les effectifs, présentes sur la frise.</summary>
    public static bool IsPlaying(RegistrationStatus status) =>
        status is RegistrationStatus.PreRegistered or RegistrationStatus.Confirmed;

    /// <summary>
    /// Équipes de la liste d'attente qu'on peut faire entrer dans une faction sans dépasser son effectif
    /// maximum, dans l'ordre d'inscription (premier inscrit, premier servi ; une équipe trop grosse est sautée).
    /// </summary>
    /// <param name="maxPlayers">Effectif maximum de la faction (0 = sans limite).</param>
    /// <param name="currentPlayers">Effectif des équipes qui participent déjà.</param>
    /// <param name="waiting">Équipes en liste d'attente, avec leur effectif et leur date d'inscription.</param>
    public static IReadOnlyList<Guid> Promotable(int maxPlayers, int currentPlayers,
        IEnumerable<(Guid TeamId, int Players, DateTimeOffset RegisteredAt)> waiting)
    {
        var result = new List<Guid>();
        var free = maxPlayers == 0 ? int.MaxValue : maxPlayers - currentPlayers;
        foreach (var (teamId, players, _) in waiting.OrderBy(w => w.RegisteredAt))
        {
            if (players > free)
                continue;
            result.Add(teamId);
            free -= players;
        }

        return result;
    }
}
