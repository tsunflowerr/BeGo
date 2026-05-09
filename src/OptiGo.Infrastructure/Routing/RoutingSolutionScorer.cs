using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Routing;

internal static class RoutingSolutionScorer
{
    public static double CalculateCompositeCost(CandidateResultDto candidate)
    {
        var metrics = candidate.Metrics;
        var score =
            0.24 * metrics.TotalGroupTimeSeconds +
            0.24 * metrics.MaxPassengerTimeSeconds +
            0.13 * metrics.MaxMemberBurdenSeconds +
            0.11 * metrics.WorstMemberRegretSeconds +
            0.10 * metrics.TotalDriverDetourSeconds +
            0.08 * metrics.MaxDriverDetourSeconds +
            0.06 * metrics.StdDriverDetourSeconds +
            0.04 * metrics.TotalWalkingTimeSeconds +
            0.03 * metrics.ArrivalSpreadSeconds +
            metrics.DriverDetourGini * 260 +
            metrics.PassengerBurdenGini * 220 +
            metrics.StopCount * 8 -
            metrics.SharedStopRate * 120;

        if (!candidate.IsFeasible)
        {
            score += candidate.FeasibilityIssues.Count * RoutingDefaults.FeasibilityIssuePenaltySeconds;
        }

        return Math.Max(0, score - candidate.ScoreBreakdown.VenueQualityBonusSeconds);
    }

    public static double CalculateTotalCostBaseline(CandidateResultDto candidate)
    {
        var metrics = candidate.Metrics;
        var score =
            metrics.TotalGroupTimeSeconds +
            0.35 * metrics.TotalDriverDetourSeconds +
            metrics.StopCount * 6;

        if (!candidate.IsFeasible)
        {
            score += candidate.FeasibilityIssues.Count * RoutingDefaults.FeasibilityIssuePenaltySeconds;
        }

        return Math.Max(0, score - candidate.ScoreBreakdown.VenueQualityBonusSeconds * 0.35);
    }

    public static SolutionMetricsDto BuildMetrics(
        Venue venue,
        IReadOnlyList<MemberRouteDto> memberRoutes,
        IReadOnlyList<DriverRouteDto> driverRoutes)
    {
        var passengerRoutes = memberRoutes
            .Where(route => route.DriverId.HasValue && route.DriverId.Value != route.MemberId)
            .ToList();
        var passengerTimes = passengerRoutes.Select(route => route.EstimatedTimeSeconds).ToList();
        var passengerBurdens = passengerRoutes
            .Select(route => route.BurdenScore > 0 ? route.BurdenScore : route.EstimatedTimeSeconds)
            .ToList();
        var memberBurdens = memberRoutes
            .Select(route => route.BurdenScore > 0 ? route.BurdenScore : route.EstimatedTimeSeconds)
            .OrderBy(value => value)
            .ToList();
        var driverDetours = driverRoutes
            .Select(route => Math.Max(0, route.TotalTimeSeconds - route.DirectTimeSeconds))
            .ToList();
        var pickupStops = driverRoutes
            .SelectMany(route => route.Stops)
            .Where(stop => stop.StopType.StartsWith("pickup", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var sharedPickupStops = pickupStops
            .Count(stop => stop.IsMergedStop || stop.PassengerIds.Count > 1);
        var arrivalTimes = driverRoutes
            .Select(route => route.TotalTimeSeconds)
            .ToList();
        var medianMemberBurden = CalculateMedian(memberBurdens);
        var maxMemberBurden = memberBurdens.DefaultIfEmpty(0).Max();

        return new SolutionMetricsDto
        {
            TotalGroupTimeSeconds = memberRoutes.Sum(route => route.EstimatedTimeSeconds),
            MaxPassengerTimeSeconds = passengerTimes.DefaultIfEmpty(0).Max(),
            StdPassengerTimeSeconds = CalculateStd(passengerTimes),
            MaxMemberBurdenSeconds = maxMemberBurden,
            WorstMemberRegretSeconds = Math.Max(0, maxMemberBurden - medianMemberBurden),
            PassengerBurdenGini = CalculateGini(passengerBurdens),
            TotalWalkingTimeSeconds = memberRoutes.Sum(route => route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond),
            MaxWalkingTimeSeconds = memberRoutes
                .Select(route => route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond)
                .DefaultIfEmpty(0)
                .Max(),
            MaxDriverDetourSeconds = driverDetours.DefaultIfEmpty(0).Max(),
            StdDriverDetourSeconds = CalculateStd(driverDetours),
            DriverDetourGini = CalculateGini(driverDetours),
            TotalDriverDetourSeconds = driverDetours.Sum(),
            ArrivalSpreadSeconds = arrivalTimes.Count <= 1 ? 0 : arrivalTimes.Max() - arrivalTimes.Min(),
            VenueRating = venue.Rating,
            StopCount = pickupStops.Count,
            SharedStopCount = sharedPickupStops,
            SharedStopRate = pickupStops.Count == 0 ? 0 : sharedPickupStops / (double)pickupStops.Count
        };
    }

    private static double CalculateMedian(IReadOnlyList<double> orderedValues)
    {
        if (orderedValues.Count == 0)
            return 0;

        var middle = orderedValues.Count / 2;
        return orderedValues.Count % 2 == 1
            ? orderedValues[middle]
            : (orderedValues[middle - 1] + orderedValues[middle]) / 2;
    }

    private static double CalculateStd(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        var average = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - average, 2)) / values.Count);
    }

    private static double CalculateGini(IReadOnlyList<double> values)
    {
        var nonNegative = values
            .Select(value => Math.Max(0, value))
            .OrderBy(value => value)
            .ToList();
        if (nonNegative.Count <= 1)
            return 0;

        var sum = nonNegative.Sum();
        if (sum <= 0)
            return 0;

        var weighted = 0.0;
        for (var i = 0; i < nonNegative.Count; i++)
        {
            weighted += (i + 1) * nonNegative[i];
        }

        return (2 * weighted) / (nonNegative.Count * sum) - (nonNegative.Count + 1) / (double)nonNegative.Count;
    }

    public static List<string> ValidateSolution(
        Session session,
        IReadOnlyList<MemberRouteDto> memberRoutes,
        IReadOnlyList<DriverRouteDto> driverRoutes,
        SolutionMetricsDto metrics)
    {
        var issues = new List<string>();
        var membersById = session.Members.ToDictionary(member => member.Id);

        foreach (var driverRoute in driverRoutes)
        {
            if (!membersById.TryGetValue(driverRoute.DriverId, out var driver))
                continue;

            if (driverRoute.PassengerIds.Count > driver.GetSeatCapacity())
            {
                issues.Add($"{driverRoute.DriverName} vượt sức chứa xe.");
            }

            var detourSeconds = Math.Max(0, driverRoute.TotalTimeSeconds - driverRoute.DirectTimeSeconds);
            if (detourSeconds > RoutingDefaults.MaxDriverDetourSeconds)
            {
                issues.Add($"{driverRoute.DriverName} phải vòng thêm quá {RoutingDefaults.MaxDriverDetourSeconds / 60:0} phút.");
            }
        }

        foreach (var route in memberRoutes.Where(route => route.DriverId.HasValue && route.DriverId.Value != route.MemberId))
        {
            if (route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond > RoutingDefaults.MaxWalkSeconds)
            {
                issues.Add($"{route.MemberName} phải đi bộ quá {RoutingDefaults.MaxWalkMinutes:0} phút.");
            }

            if (route.EstimatedTimeSeconds > RoutingDefaults.MaxPassengerTotalTravelSeconds)
            {
                issues.Add($"{route.MemberName} có tổng thời gian vượt {RoutingDefaults.MaxPassengerTotalTravelSeconds / 60:0} phút.");
            }
        }

        var passengerTimes = memberRoutes
            .Where(route => route.DriverId.HasValue && route.DriverId.Value != route.MemberId)
            .Select(route => route.EstimatedTimeSeconds)
            .OrderBy(time => time)
            .ToList();
        if (passengerTimes.Count >= 3)
        {
            var median = passengerTimes[passengerTimes.Count / 2];
            if (metrics.MaxPassengerTimeSeconds > median * 1.6)
            {
                issues.Add("Chênh lệch thời gian passenger quá lớn so với median.");
            }
        }

        var arrivalTimes = driverRoutes
            .Select(route => route.TotalTimeSeconds)
            .ToList();
        if (arrivalTimes.Count > 1 &&
            arrivalTimes.Max() - arrivalTimes.Min() > RoutingDefaults.ArrivalSpreadSoftLimitSeconds)
        {
            issues.Add("Các xe đến venue lệch nhau quá 10 phút.");
        }

        return issues.Distinct(StringComparer.Ordinal).ToList();
    }

    public static double CalculateVenueQualityBonusSeconds(Venue venue)
    {
        if (venue.Rating <= 0)
            return 0;

        var reviewWeight = Math.Min(1.0, venue.ReviewCount / 400.0);
        var ratingWeight = Math.Max(0, (venue.Rating - 3.8) / 1.2);
        return Math.Min(RoutingDefaults.QualityBonusCapSeconds, reviewWeight * ratingWeight * RoutingDefaults.QualityBonusCapSeconds);
    }
}
