using System;
using System.Collections.Generic;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Finance;
using AirsoftPlanner.Data;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Traces GPS des véhicules mis en jeu, partagées entre le suivi, les équipes et les finances.</summary>
public class VehicleTracker(OperationFile file)
{
    private readonly List<VehiclePosition> _positions = file.LoadVehiclePositions().ToList();

    /// <summary>Déclenché à chaque position reçue (kilométrage et carte à mettre à jour).</summary>
    public event Action? Changed;

    public void Add(TeamVehicle vehicle, GeoPoint point, DateTimeOffset at)
    {
        var position = new VehiclePosition { VehicleId = vehicle.Id, Point = point, At = at };
        file.Add(position);
        _positions.Add(position);
        Changed?.Invoke();
    }

    public double TrackKilometers(Guid vehicleId) =>
        VehicleMileage.TrackKilometers(_positions.Where(p => p.VehicleId == vehicleId).Select(p => (p.Point, p.At)));

    /// <summary>Kilomètres retenus : compteur s'il est relevé, sinon trace GPS.</summary>
    public double Kilometers(TeamVehicle vehicle) =>
        VehicleMileage.Kilometers(vehicle.OdometerStartKm, vehicle.OdometerEndKm, TrackKilometers(vehicle.Id));

    public GeoPoint? LastPosition(Guid vehicleId, DateTimeOffset before) =>
        _positions.Where(p => p.VehicleId == vehicleId && p.At <= before).MaxBy(p => p.At)?.Point;
}
