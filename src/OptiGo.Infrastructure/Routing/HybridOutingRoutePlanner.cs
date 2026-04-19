using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Routing;

public class HybridOutingRoutePlanner : IOutingRoutePlanner
{
    public const string PlannerVersionName = "hybrid-v2";

    private readonly IDriverRouteOptimizer _driverRouteOptimizer;
    private readonly IRouteCostProvider _routeCostProvider;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;

    public HybridOutingRoutePlanner(
        IDriverRouteOptimizer driverRouteOptimizer,
        IRouteCostProvider routeCostProvider,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
        _driverRouteOptimizer = driverRouteOptimizer;
        _routeCostProvider = routeCostProvider;
        _trafficSnapshotProvider = trafficSnapshotProvider;
    }

    public async Task<CandidateResultDto> PlanVenueAsync(
        Session session,
        Venue venue,
        CancellationToken ct = default)
    {
        var before = _routeCostProvider.CaptureSnapshot();
        var trafficSnapshot = _trafficSnapshotProvider.GetCurrentSnapshot();
        var membersById = session.Members.ToDictionary(member => member.Id);
        var acceptedRequests = session.PickupRequests
            .Where(request => request.IsAccepted())
            .ToList();
        var requestsByDriver = acceptedRequests
            .GroupBy(request => request.AcceptedDriverId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var assignedPassengerIds = acceptedRequests.Select(request => request.PassengerId).ToHashSet();

        var driverRoutes = new List<DriverRouteDto>();
        var memberRoutes = new List<MemberRouteDto>();
        var aggregateBreakdown = new RouteScoreBreakdownDto();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            requestsByDriver.TryGetValue(driver.Id, out var driverRequests);
            var passengers = driverRequests?
                .Select(request => membersById[request.PassengerId])
                .ToList() ?? [];

            var optimized = await _driverRouteOptimizer.OptimizeAsync(
                new DriverOptimizationInput
                {
                    Driver = driver,
                    Passengers = passengers,
                    Venue = venue,
                    TrafficSnapshot = trafficSnapshot,
                    PreferTrafficAwareRoutes = false
                },
                ct);

            driverRoutes.Add(optimized.DriverRoute);
            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = driver.Id,
                MemberName = driver.Name,
                EstimatedTimeSeconds = optimized.DriverRoute.TotalTimeSeconds,
                DistanceMeters = optimized.DriverRoute.TotalDistanceMeters,
                RideDistanceMeters = optimized.DriverRoute.TotalDistanceMeters,
                RideTimeSeconds = optimized.DriverRoute.TotalTimeSeconds,
                DriverId = driver.Id,
                BurdenScore = optimized.DriverRoute.GeneralizedCostSeconds
            });
            memberRoutes.AddRange(optimized.PassengerRoutes);
            AggregateBreakdown(aggregateBreakdown, optimized.CostBreakdown);
        }

        foreach (var member in session.Members.Where(member =>
                     !member.CanOfferPickup() &&
                     !assignedPassengerIds.Contains(member.Id)))
        {
            var directRoute = await _routeCostProvider.GetExactRouteAsync(
                member.GetLocation(),
                venue.GetLocation(),
                member.TransportMode,
                new RouteCostContext(false, trafficSnapshot.BucketKey),
                ct);

            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = member.Id,
                MemberName = member.Name,
                EstimatedTimeSeconds = directRoute.DurationSeconds,
                DistanceMeters = directRoute.DistanceMeters,
                RideDistanceMeters = directRoute.DistanceMeters,
                RideTimeSeconds = directRoute.DurationSeconds,
                DriverId = null,
                WalkingDistanceMeters = 0,
                WaitTimeSeconds = 0,
                BurdenScore = directRoute.DurationSeconds
            });

            aggregateBreakdown.GeneralizedCostSeconds += directRoute.DurationSeconds;
            aggregateBreakdown.TotalDriveSeconds += directRoute.DurationSeconds;
        }

        var qualityBonusSeconds = CalculateVenueQualityBonusSeconds(venue);
        aggregateBreakdown.VenueQualityBonusSeconds = qualityBonusSeconds;
        aggregateBreakdown.GeneralizedCostSeconds = Math.Max(0, aggregateBreakdown.GeneralizedCostSeconds - qualityBonusSeconds);

        var after = _routeCostProvider.CaptureSnapshot();
        var diff = RouteDiagnosticsSnapshot.Diff(before, after);

        return new CandidateResultDto
        {
            VenueId = venue.Id,
            Name = venue.Name,
            Category = venue.Category,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            Address = venue.Address,
            Rating = venue.Rating,
            ReviewCount = venue.ReviewCount,
            TotalTimeSeconds = memberRoutes.Sum(route => route.EstimatedTimeSeconds),
            MaxDriverDetourSeconds = driverRoutes
                .Select(route => Math.Max(0, route.TotalTimeSeconds - route.DirectTimeSeconds))
                .DefaultIfEmpty(0)
                .Max(),
            TotalWalkingDistanceMeters = memberRoutes.Sum(route => route.WalkingDistanceMeters),
            PlannerVersion = PlannerVersionName,
            ApiCostEstimate = diff.TotalApiCostEstimate,
            CacheHitRatio = diff.CacheHitRatio,
            CacheDiagnostics = diff.ToDto(),
            ScoreBreakdown = aggregateBreakdown,
            MemberRoutes = memberRoutes.OrderBy(route => route.MemberName).ToList(),
            DriverRoutes = driverRoutes.OrderBy(route => route.DriverName).ToList()
        };
    }

    private static void AggregateBreakdown(RouteScoreBreakdownDto aggregate, RouteScoreBreakdownDto current)
    {
        aggregate.GeneralizedCostSeconds += current.GeneralizedCostSeconds;
        aggregate.TotalDriveSeconds += current.TotalDriveSeconds;
        aggregate.TotalWalkSeconds += current.TotalWalkSeconds;
        aggregate.TotalWaitSeconds += current.TotalWaitSeconds;
        aggregate.DetourPenaltySeconds += current.DetourPenaltySeconds;
        aggregate.FairnessPenaltySeconds += current.FairnessPenaltySeconds;
        aggregate.StopComplexityPenaltySeconds += current.StopComplexityPenaltySeconds;
        aggregate.RiskPenaltySeconds += current.RiskPenaltySeconds;
        aggregate.StabilityPenaltySeconds += current.StabilityPenaltySeconds;
    }

    private static double CalculateVenueQualityBonusSeconds(Venue venue)
    {
        if (venue.Rating <= 0)
            return 0;

        var reviewWeight = Math.Min(1.0, venue.ReviewCount / 400.0);
        var ratingWeight = Math.Max(0, (venue.Rating - 3.8) / 1.2);
        return Math.Min(RoutingDefaults.QualityBonusCapSeconds, reviewWeight * ratingWeight * RoutingDefaults.QualityBonusCapSeconds);
    }
}
