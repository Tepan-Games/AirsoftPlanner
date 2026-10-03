using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;

namespace AirsoftPlanner.Core.Finance;

/// <summary>Kilomètres parcourus par un véhicule pendant l'OP, pour lui rembourser le carburant.</summary>
public static class VehicleMileage
{
    /// <summary>En deçà, un écart entre deux positions est considéré comme l'imprécision du GPS (véhicule à l'arrêt).</summary>
    public const double JitterMeters = 15;

    /// <summary>Au-delà, un saut entre deux positions est un point aberrant (ignoré).</summary>
    public const double MaxPlausibleKmh = 150;

    /// <summary>Distance parcourue le long d'une trace GPS, en kilomètres.</summary>
    public static double TrackKilometers(IEnumerable<(GeoPoint Point, DateTimeOffset At)> track)
    {
        var meters = 0.0;
        (GeoPoint Point, DateTimeOffset At)? last = null;
        foreach (var fix in track.OrderBy(f => f.At))
        {
            if (last is not { } previous)
            {
                last = fix;
                continue;
            }

            var distance = GeoMath.DistanceMeters(previous.Point, fix.Point);
            var hours = Math.Max((fix.At - previous.At).TotalHours, 1e-6);
            if (distance < JitterMeters)
                continue; // on garde le point de référence : les petits écarts ne s'additionnent pas
            if (distance / 1000 / hours > MaxPlausibleKmh)
                continue; // point aberrant : ignoré, la référence ne bouge pas
            meters += distance;
            last = fix;
        }

        return meters / 1000;
    }

    /// <summary>
    /// Kilomètres retenus : relevé du compteur (arrivée − départ) s'il est complet, sinon la trace GPS.
    /// </summary>
    public static double Kilometers(decimal? odometerStart, decimal? odometerEnd, double trackKilometers) =>
        odometerStart is { } start && odometerEnd is { } end && end >= start ? (double)(end - start) : trackKilometers;

    /// <summary>Carburant remboursé : kilomètres × tarif au kilomètre, arrondi au centime.</summary>
    public static decimal FuelRefund(double kilometers, decimal ratePerKm) =>
        Math.Round((decimal)kilometers * ratePerKm, 2, MidpointRounding.AwayFromZero);
}
