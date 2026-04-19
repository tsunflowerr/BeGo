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

public sealed record RouteDiagnosticsSnapshot(
    long CacheHits,
    long CacheMisses,
    long ExactRouteApiCalls,
    long MatrixApiCalls)
{
    public CacheDiagnosticsDto ToDto() => new()
    {
        CacheHits = CacheHits,
        CacheMisses = CacheMisses,
        ExactRouteApiCalls = ExactRouteApiCalls,
        MatrixApiCalls = MatrixApiCalls
    };

    public double TotalApiCostEstimate => ExactRouteApiCalls + MatrixApiCalls * 0.35;

    public double CacheHitRatio =>
        CacheHits + CacheMisses == 0
            ? 1
            : (double)CacheHits / (CacheHits + CacheMisses);

    public static RouteDiagnosticsSnapshot Diff(RouteDiagnosticsSnapshot before, RouteDiagnosticsSnapshot after) =>
        new(
            Math.Max(0, after.CacheHits - before.CacheHits),
            Math.Max(0, after.CacheMisses - before.CacheMisses),
            Math.Max(0, after.ExactRouteApiCalls - before.ExactRouteApiCalls),
            Math.Max(0, after.MatrixApiCalls - before.MatrixApiCalls));
}

public sealed record RouteCostContext(
    bool PreferTrafficAware,
    string TrafficBucketKey);
