using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class GeometricMedianCalculator
{
    private const double EpsilonMeters = 1.0;
    private const int MaxIterations = 50;

    public static Coordinate Calculate(IReadOnlyList<Coordinate> origins)
    {
        if (origins == null || origins.Count == 0)
            throw new ArgumentException("Origins cannot be null or empty.", nameof(origins));

        if (origins.Count == 1)
            return origins[0];

        var current = CalculateCentroid(origins);

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            double numeratorLat = 0;
            double numeratorLng = 0;
            double denominator = 0;

            foreach (var origin in origins)
            {
                var distance = Math.Max(current.DistanceTo(origin), EpsilonMeters);
                var factor = 1.0 / distance;

                numeratorLat += origin.Latitude * factor;
                numeratorLng += origin.Longitude * factor;
                denominator += factor;
            }

            var next = new Coordinate(numeratorLat / denominator, numeratorLng / denominator);
            if (current.DistanceTo(next) < EpsilonMeters)
                return next;

            current = next;
        }

        return current;
    }

    private static Coordinate CalculateCentroid(IReadOnlyList<Coordinate> origins)
    {
        var latitude = origins.Average(origin => origin.Latitude);
        var longitude = origins.Average(origin => origin.Longitude);
        return new Coordinate(latitude, longitude);
    }
}
