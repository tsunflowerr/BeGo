using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class GeometricMedianCalculator
{
    private const double Epsilon = 1e-6;
    private const int MaxIterations = 100;

    public static Coordinate Calculate(IReadOnlyList<Coordinate> origins)
    {
        if (origins == null || !origins.Any())
            throw new ArgumentException("Origins cannot be null or empty.");

        if (origins.Count == 1)
            return origins[0];

        var currentPoint = CalculateCentroid(origins);

        for (int i = 0; i < MaxIterations; i++)
        {
            double numeratorLat = 0;
            double numeratorLon = 0;
            double denominator = 0;

            foreach (var origin in origins)
            {

                var distance = currentPoint.DistanceTo(origin);

                var weight = distance == 0 ? 1 / Epsilon : 1 / distance;

                numeratorLat += origin.Latitude * weight;
                numeratorLon += origin.Longitude * weight;
                denominator += weight;
            }

            var nextLat = numeratorLat / denominator;
            var nextLon = numeratorLon / denominator;
            var nextPoint = new Coordinate(nextLat, nextLon);

            if (currentPoint.DistanceTo(nextPoint) < Epsilon)
            {
                return nextPoint;
            }

            currentPoint = nextPoint;
        }

        return currentPoint;
    }

    private static Coordinate CalculateCentroid(IReadOnlyList<Coordinate> origins)
    {
        var avgLat = origins.Average(o => o.Latitude);
        var avgLon = origins.Average(o => o.Longitude);
        return new Coordinate(avgLat, avgLon);
    }
}
