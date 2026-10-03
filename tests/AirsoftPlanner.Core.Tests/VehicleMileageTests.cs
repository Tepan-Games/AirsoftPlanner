using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Finance;

namespace AirsoftPlanner.Core.Tests;

public class VehicleMileageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 9, 0, 0, TimeSpan.FromHours(2));

    // 0,009° de latitude ≈ 1 km
    private static (GeoPoint, DateTimeOffset) At(double kmNorth, int minutes, double jitterMeters = 0) =>
        (new GeoPoint(45 + kmNorth * 0.009 + jitterMeters / 111_000, 5), T0.AddMinutes(minutes));

    [Fact]
    public void Track_distance_adds_up_movements()
    {
        var km = VehicleMileage.TrackKilometers([At(0, 0), At(1, 5), At(3, 10), At(3, 30)]);

        Assert.InRange(km, 2.95, 3.05);
    }

    [Fact]
    public void Gps_jitter_while_parked_is_not_counted()
    {
        var parked = Enumerable.Range(0, 60).Select(i => At(0, i, jitterMeters: i % 2 == 0 ? 0 : 8)).ToList();

        Assert.Equal(0, VehicleMileage.TrackKilometers(parked), 3);
    }

    [Fact]
    public void Implausible_jumps_are_ignored()
    {
        // Saut de 50 km en 1 minute : point aberrant.
        var km = VehicleMileage.TrackKilometers([At(0, 0), At(50, 1), At(1, 5)]);

        Assert.InRange(km, 0.95, 1.05);
    }

    [Fact]
    public void Odometer_reading_wins_over_the_track_when_complete()
    {
        Assert.Equal(42, VehicleMileage.Kilometers(12_000, 12_042, 30));
        Assert.Equal(30, VehicleMileage.Kilometers(12_000, null, 30));
        Assert.Equal(30, VehicleMileage.Kilometers(12_050, 12_000, 30)); // relevé incohérent : trace GPS
    }

    [Fact]
    public void Fuel_refund_is_rounded_to_the_cent()
    {
        Assert.Equal(12.35m, VehicleMileage.FuelRefund(61.73, 0.20m));
    }
}
