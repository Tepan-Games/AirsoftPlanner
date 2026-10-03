using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Planning;

public enum ProgressStatus
{
    /// <summary>Aucune position reçue.</summary>
    Unknown,

    /// <summary>Aucune mission en cours ni à venir.</summary>
    Idle,

    /// <summary>L'équipe a le temps de rejoindre sa prochaine zone.</summary>
    OnTime,

    /// <summary>L'équipe arrivera de justesse (marge inférieure au seuil).</summary>
    Tight,

    /// <summary>L'équipe n'aura pas le temps de rejoindre sa zone, ou n'y est pas alors que la mission a commencé.</summary>
    Late,
}

/// <param name="TeamId">Équipe.</param>
/// <param name="Status">État de l'équipe par rapport au planning.</param>
/// <param name="CurrentMission">Mission en cours, le cas échéant.</param>
/// <param name="TargetMission">Mission dont la zone doit être rejointe (en cours ou prochaine).</param>
/// <param name="DistanceMeters">Distance entre la dernière position et la zone de la mission visée.</param>
/// <param name="TravelMinutes">Temps de trajet estimé jusqu'à cette zone.</param>
/// <param name="SlackMinutes">Marge restante : temps avant le début de la mission moins le trajet (négatif = retard).</param>
/// <param name="PositionAgeMinutes">Ancienneté de la dernière position.</param>
public record TeamProgress(
    Guid TeamId,
    ProgressStatus Status,
    Mission? CurrentMission,
    Mission? TargetMission,
    double? DistanceMeters,
    double? TravelMinutes,
    double? SlackMinutes,
    double? PositionAgeMinutes);

/// <summary>Évalue, à un instant donné, si chaque équipe peut tenir le planning compte tenu de sa position.</summary>
public static class ProgressTracker
{
    /// <summary>En dessous de cette marge, l'équipe est signalée « juste ».</summary>
    public const double TightSlackMinutes = 10;

    /// <summary>Distance en deçà de laquelle une équipe est considérée sur sa zone (imprécision GPS, taille du point).</summary>
    public const double ArrivalToleranceMeters = 30;

    /// <param name="teamId">Équipe évaluée.</param>
    /// <param name="missions">Toutes les missions (les désactivées sont ignorées).</param>
    /// <param name="zones">Zones par identifiant.</param>
    /// <param name="lastPosition">Dernière position connue de l'équipe, avec son heure (minutes depuis minuit le jour de l'OP).</param>
    /// <param name="nowMinutes">Instant d'évaluation, en minutes depuis minuit le jour de l'OP.</param>
    /// <param name="walkingSpeedKmh">Vitesse de déplacement retenue.</param>
    public static TeamProgress Evaluate(
        Guid teamId,
        IEnumerable<Mission> missions,
        IReadOnlyDictionary<Guid, Zone> zones,
        (GeoPoint Point, double AtMinutes)? lastPosition,
        double nowMinutes,
        double walkingSpeedKmh)
    {
        var own = missions.Where(m => m.IsEnabled && m.TeamIds.Contains(teamId)).OrderBy(m => m.StartMinutes).ToList();
        var current = own.FirstOrDefault(m => m.StartMinutes <= nowMinutes && nowMinutes < m.EndMinutes);
        var next = own.FirstOrDefault(m => m.StartMinutes > nowMinutes);
        var target = current ?? next;
        var age = lastPosition is { } known ? nowMinutes - known.AtMinutes : (double?)null;

        if (lastPosition is not { } position)
            return new TeamProgress(teamId, ProgressStatus.Unknown, current, target, null, null, null, null);
        if (target is null)
            return new TeamProgress(teamId, ProgressStatus.Idle, current, null, null, null, null, age);
        if (target.ZoneId is not { } zoneId || !zones.TryGetValue(zoneId, out var zone) || zone.Points.Count == 0)
            return new TeamProgress(teamId, ProgressStatus.OnTime, current, target, null, null, null, age);

        var distance = GeoMath.DistanceToZone(position.Point, zone);
        if (distance <= ArrivalToleranceMeters)
            distance = 0;
        var travel = distance / (walkingSpeedKmh * 1000 / 60);
        var slack = target.StartMinutes - nowMinutes - travel;
        // Mission en cours : l'équipe doit être sur sa zone. Sinon : elle doit avoir le temps de la rejoindre.
        var status = current is not null ? (distance > 0 ? ProgressStatus.Late : ProgressStatus.OnTime)
            : slack < 0 ? ProgressStatus.Late
            : slack < TightSlackMinutes && distance > 0 ? ProgressStatus.Tight
            : ProgressStatus.OnTime;
        return new TeamProgress(teamId, status, current, target, distance, travel, slack, age);
    }
}
