using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class WeightedGeometricMedianCalculator
{
    private const double EpsilonMeters = 1.0;
    private const double ConvergenceThresholdMeters = 5.0;
    private const int MaxIterations = 50;
    private const double ReferenceSpeedKmph = 25.0;
    private const double BlendFactor = 0.35;

    public static Coordinate Calculate(IReadOnlyList<Member> members)
    {
        if (members == null || members.Count == 0)
            throw new ArgumentException("Members cannot be null or empty.", nameof(members));

        if (members.Count == 1)
            return members[0].GetLocation();

        var weightedPoints = members
            .Select(member => new WeightedPoint(member.GetLocation(), GetMemberWeight(member)))
            .ToList();

        var baseline = GeometricMedianCalculator.Calculate(
            members.Select(member => member.GetLocation()).ToList());

        var current = CalculateInitialPoint(weightedPoints);

        for (int i = 0; i < MaxIterations; i++)
        {
            var next = CalculateNextPoint(current, weightedPoints);

            if (current.DistanceTo(next) < ConvergenceThresholdMeters)
                return BlendTowardBaseline(baseline, next);

            current = next;
        }

        return BlendTowardBaseline(baseline, current);
    }

    private static double GetMemberWeight(Member member)
    {
        var speed = GetModeSpeedKmph(member.TransportMode);
        var ratio = ReferenceSpeedKmph / speed;
        var rawWeight = 1.0 + 0.35 * (ratio - 1.0);
        return Math.Clamp(rawWeight, 0.95, 1.75);
    }

    private static double GetModeSpeedKmph(TransportMode mode) => mode switch
    {
        TransportMode.Walking => 4.5,
        TransportMode.Cycling => 12.0,
        TransportMode.Bus => 18.0,
        TransportMode.Motorbike => 28.0,
        TransportMode.Car => 25.0,
        _ => 25.0
    };

    private static Coordinate CalculateInitialPoint(IReadOnlyList<WeightedPoint> points)
    {
        var totalWeight = points.Sum(p => p.Weight);
        var lat = points.Sum(p => p.Point.Latitude * p.Weight) / totalWeight;
        var lng = points.Sum(p => p.Point.Longitude * p.Weight) / totalWeight;
        return new Coordinate(lat, lng);
    }

    private static Coordinate CalculateNextPoint(Coordinate current, IReadOnlyList<WeightedPoint> points)
    {
        double numeratorLat = 0;
        double numeratorLng = 0;
        double denominator = 0;

        foreach (var item in points)
        {
            var distance = Math.Max(current.DistanceTo(item.Point), EpsilonMeters);
            var factor = item.Weight / distance;
            numeratorLat += item.Point.Latitude * factor;
            numeratorLng += item.Point.Longitude * factor;
            denominator += factor;
        }

        return new Coordinate(numeratorLat / denominator, numeratorLng / denominator);
    }

    private static Coordinate BlendTowardBaseline(Coordinate baseline, Coordinate weighted)
    {
        var latitude = baseline.Latitude + (weighted.Latitude - baseline.Latitude) * BlendFactor;
        var longitude = baseline.Longitude + (weighted.Longitude - baseline.Longitude) * BlendFactor;
        return new Coordinate(latitude, longitude);
    }

    private readonly record struct WeightedPoint(Coordinate Point, double Weight);
}