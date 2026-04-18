using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class CandidateFilter
{
    private const double AverageSpeedMetersPerSecond = 11.0;

    public static IReadOnlyList<Venue> FilterTopCandidates(
        IReadOnlyList<Member> members,
        IReadOnlyList<Venue> rawVenues,
        int topN = 25)
    {
        if (rawVenues.Count <= topN)
            return rawVenues;

        var venueEstimates = new List<(Venue Venue, double EstimateScore)>();

        foreach (var venue in rawVenues)
        {
            var estimatedTimes = new List<double>();
            foreach (var member in members)
            {
                estimatedTimes.Add(EstimateTravelSeconds(member, venue));
            }

            var score = estimatedTimes.Max() + estimatedTimes.Sum();

            venueEstimates.Add((venue, score));
        }

        return venueEstimates
            .OrderBy(v => v.EstimateScore)
            .Take(topN)
            .Select(v => v.Venue)
            .ToList();
    }

    private static double EstimateTravelSeconds(
        Member member,
        Venue venue)
    {
        return EstimateDirectTravelSeconds(member.GetLocation(), venue.GetLocation(), member.TransportMode);
    }

    private static double EstimateDirectTravelSeconds(Coordinate origin, Coordinate destination, Enums.TransportMode mode)
    {
        var distanceMeters = origin.DistanceTo(destination);
        var estimatedSeconds = distanceMeters / AverageSpeedMetersPerSecond;
        return estimatedSeconds * GetSimpleTransportFactor(mode);
    }

    private static double GetSimpleTransportFactor(Enums.TransportMode mode) => mode switch
    {
        Enums.TransportMode.Walking => 4.0,
        Enums.TransportMode.Cycling => 2.0,
        Enums.TransportMode.Motorbike => 0.85,
        Enums.TransportMode.Car => 1.0,
        Enums.TransportMode.Bus => 1.8,
        _ => 1.0
    };
}
