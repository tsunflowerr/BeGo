using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.UseCases;

public class DriverOptimizationInput
{
    public required Member Driver { get; init; }
    public required IReadOnlyList<Member> Passengers { get; init; }
    public required Venue Venue { get; init; }
    public required TrafficSnapshot TrafficSnapshot { get; init; }
    public bool PreferTrafficAwareRoutes { get; init; }
}

public class DriverOptimizationResult
{
    public required DriverRouteDto DriverRoute { get; init; }
    public required IReadOnlyList<MemberRouteDto> PassengerRoutes { get; init; }
    public required RouteScoreBreakdownDto CostBreakdown { get; init; }
}

public class StopCandidate
{
    public string CandidateId { get; init; } = Guid.NewGuid().ToString("N");
    public required Coordinate StopLocation { get; init; }
    public required string Label { get; init; }
    public required string StopAccessType { get; init; }
    public required IReadOnlyList<Guid> PassengerIds { get; init; }
    public required IReadOnlyDictionary<Guid, double> WalkingDistancesMeters { get; init; }
    public double AccessPenaltySeconds { get; init; }
    public double RiskPenaltySeconds { get; init; }
    public bool IsMergedStop => PassengerIds.Count > 1;
}

public sealed record TrafficSnapshot(
    string BucketKey,
    double CongestionMultiplier = 1.0,
    bool IsLive = false);

public sealed record RouteCostContext(
    bool PreferTrafficAware,
    string TrafficBucketKey);
