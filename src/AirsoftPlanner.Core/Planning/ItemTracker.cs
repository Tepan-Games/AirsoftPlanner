using AirsoftPlanner.Core.Domain;

namespace AirsoftPlanner.Core.Planning;

/// <param name="Kind">Dernier événement connu, ou null si l'objet n'a encore aucun historique.</param>
/// <param name="HolderTeamId">Équipe qui détient l'objet, le cas échéant.</param>
/// <param name="Location">Où l'objet se trouve : position de l'événement, ou dernière position de l'équipe qui le détient.</param>
/// <param name="Since">Heure du dernier événement.</param>
/// <param name="OnField">Vrai tant que l'objet reste à récupérer sur le terrain (ni rendu, ni jamais placé).</param>
public record ItemState(ItemEventKind? Kind, Guid? HolderTeamId, GeoPoint? Location, DateTimeOffset? Since, bool OnField);

/// <summary>Où en est chaque objet d'objectif à un instant donné.</summary>
public static class ItemTracker
{
    /// <param name="events">Historique de l'objet.</param>
    /// <param name="at">Instant considéré (les événements postérieurs sont ignorés, pour rejouer l'OP).</param>
    /// <param name="teamPosition">Dernière position connue d'une équipe avant un instant donné.</param>
    public static ItemState StateAt(IEnumerable<ItemEvent> events, DateTimeOffset at,
        Func<Guid, DateTimeOffset, GeoPoint?> teamPosition)
    {
        var last = events.Where(e => e.At <= at).OrderBy(e => e.At).LastOrDefault();
        if (last is null)
            return new ItemState(null, null, null, null, OnField: false);

        var holder = last.Kind is ItemEventKind.PickedUp or ItemEventKind.Transferred ? last.TeamId : null;
        // Objet détenu par une équipe : il se trouve là où l'équipe a été vue en dernier.
        var location = holder is { } team ? teamPosition(team, at) ?? last.Location : last.Location;
        return new ItemState(last.Kind, holder, location, last.At, OnField: last.Kind != ItemEventKind.Returned);
    }
}
