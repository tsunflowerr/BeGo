using System;

namespace OptiGo.Domain.ValueObjects;

public record struct Coordinate
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }

    public Coordinate(double latitude, double longitude)
    {
        if (latitude is < -90.0 or > 90.0)
            throw new ArgumentException("Latitude must be between -90 and 90 degrees.", nameof(latitude));

        if (longitude is < -180.0 or > 180.0)
            throw new ArgumentException("Longitude must be between -180 and 180 degrees.", nameof(longitude));

        Latitude = latitude;
        Longitude = longitude;
    }

    public double DistanceTo(Coordinate other)
    {
        var R = 6371e3;
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);
        var lat1 = ToRadians(Latitude);
        var lat2 = ToRadians(other.Latitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2) * Math.Cos(lat1) * Math.Cos(lat2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double angle) => angle * Math.PI / 180.0;
}
