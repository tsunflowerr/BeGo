using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class BaselineOutingRoutePlanner : IBaselineOutingRoutePlanner
{
    public const string PlannerVersionName = "heuristic-v1";

    private readonly IRouteCostProvider _routeCostProvider;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;

    public BaselineOutingRoutePlanner(
        IRouteCostProvider routeCostProvider,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
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
            var result = await BuildDriverRouteAsync(driver, driverRequests ?? [], membersById, venue, trafficSnapshot, ct);
            driverRoutes.Add(result.Route);
            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = driver.Id,
                MemberName = driver.Name,
                EstimatedTimeSeconds = result.Route.TotalTimeSeconds,
                DistanceMeters = result.Route.TotalDistanceMeters,
                RideDistanceMeters = result.Route.TotalDistanceMeters,
                RideTimeSeconds = result.Route.TotalTimeSeconds,
                DriverId = driver.Id,
                BurdenScore = result.Route.GeneralizedCostSeconds
            });
            memberRoutes.AddRange(result.PassengerRoutes);
            AggregateBreakdown(aggregateBreakdown, result.CostBreakdown);
        }

        foreach (var member in session.Members.Where(member =>
                     !member.CanOfferPickup() &&
                     !assignedPassengerIds.Contains(member.Id)))
        {
            var directRoute = await GetRouteAsync(
                member.GetLocation(),
                venue.GetLocation(),
                member.TransportMode,
                trafficSnapshot,
                ct);

            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = member.Id,
                MemberName = member.Name,
                EstimatedTimeSeconds = directRoute.DurationSeconds,
                DistanceMeters = directRoute.DistanceMeters,
                RideDistanceMeters = directRoute.DistanceMeters,
                RideTimeSeconds = directRoute.DurationSeconds,
                BurdenScore = directRoute.DurationSeconds
            });

            aggregateBreakdown.GeneralizedCostSeconds += directRoute.DurationSeconds;
            aggregateBreakdown.TotalDriveSeconds += directRoute.DurationSeconds;
        }

        aggregateBreakdown.VenueQualityBonusSeconds = CalculateVenueQualityBonusSeconds(venue);
        aggregateBreakdown.GeneralizedCostSeconds = Math.Max(0, aggregateBreakdown.GeneralizedCostSeconds - aggregateBreakdown.VenueQualityBonusSeconds);

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

    private async Task<DriverRouteBuildResult> BuildDriverRouteAsync(
        Member driver,
        IReadOnlyList<PickupRequest> driverRequests,
        IReadOnlyDictionary<Guid, Member> membersById,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var venueLocation = venue.GetLocation();
        var directRoute = await GetRouteAsync(driver.GetLocation(), venueLocation, driver.TransportMode, trafficSnapshot, ct);
        var candidateStops = new List<PickupStopCandidate>();

        foreach (var request in driverRequests)
        {
            var passenger = membersById[request.PassengerId];
            candidateStops.Add(await SelectPickupStopAsync(driver, passenger, venueLocation, trafficSnapshot, ct));
        }

        var orderedStops = await OrderStopsAsync(driver, candidateStops, trafficSnapshot, ct);
        var routeStops = new List<RouteStopDto>
        {
            new()
            {
                Sequence = 0,
                StopType = "driver_origin",
                Label = driver.Name,
                Latitude = driver.Latitude,
                Longitude = driver.Longitude,
                EtaSeconds = 0,
                DistanceFromPreviousMeters = 0,
                CumulativeDistanceMeters = 0,
                CumulativeTimeSeconds = 0,
                StopAccessType = "origin"
            }
        };
        var passengerRoutes = new List<MemberRouteDto>();
        var pickupSnapshots = new Dictionary<Guid, PickupSnapshot>();

        var current = driver.GetLocation();
        double elapsedSeconds = 0;
        double elapsedDistanceMeters = 0;
        double walkSecondsTotal = 0;
        double waitSecondsTotal = 0;

        foreach (var stop in orderedStops)
        {
            var leg = await GetRouteAsync(current, stop.StopLocation, driver.TransportMode, trafficSnapshot, ct);
            elapsedSeconds += leg.DurationSeconds;
            elapsedDistanceMeters += leg.DistanceMeters;
            current = stop.StopLocation;

            var waitSeconds = EstimateWaitSeconds(elapsedSeconds, stop.WalkingDistanceMeters);
            routeStops.Add(new RouteStopDto
            {
                Sequence = routeStops.Count,
                StopType = stop.WalkingDistanceMeters > 0 ? "pickup_meetpoint" : "pickup",
                Label = stop.Label,
                Latitude = stop.StopLocation.Latitude,
                Longitude = stop.StopLocation.Longitude,
                EtaSeconds = elapsedSeconds,
                DistanceFromPreviousMeters = leg.DistanceMeters,
                CumulativeDistanceMeters = elapsedDistanceMeters,
                CumulativeTimeSeconds = elapsedSeconds,
                WalkingDistanceMeters = stop.WalkingDistanceMeters,
                WaitSeconds = waitSeconds,
                StopAccessType = stop.WalkingDistanceMeters > 0 ? "approximate_roadside" : "doorstep",
                PassengerIds = [stop.PassengerId]
            });

            pickupSnapshots[stop.PassengerId] = new PickupSnapshot(elapsedSeconds, elapsedDistanceMeters, stop.WalkingDistanceMeters, waitSeconds);
            walkSecondsTotal += stop.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
            waitSecondsTotal += waitSeconds;
        }

        var venueLeg = await GetRouteAsync(current, venueLocation, driver.TransportMode, trafficSnapshot, ct);
        elapsedSeconds += venueLeg.DurationSeconds;
        elapsedDistanceMeters += venueLeg.DistanceMeters;
        routeStops.Add(new RouteStopDto
        {
            Sequence = routeStops.Count,
            StopType = "destination",
            Label = venue.Name,
            Latitude = venueLocation.Latitude,
            Longitude = venueLocation.Longitude,
            EtaSeconds = elapsedSeconds,
            DistanceFromPreviousMeters = venueLeg.DistanceMeters,
            CumulativeDistanceMeters = elapsedDistanceMeters,
            CumulativeTimeSeconds = elapsedSeconds,
            StopAccessType = "destination"
        });

        foreach (var stop in orderedStops)
        {
            var snapshot = pickupSnapshots[stop.PassengerId];
            var rideTimeSeconds = Math.Max(0, elapsedSeconds - snapshot.CumulativeTimeSeconds);
            var rideDistanceMeters = Math.Max(0, elapsedDistanceMeters - snapshot.CumulativeDistanceMeters);
            var walkingSeconds = stop.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
            var burdenScore =
                rideTimeSeconds +
                walkingSeconds * RoutingDefaults.WalkWeight +
                snapshot.WaitSeconds * RoutingDefaults.WaitWeight;

            passengerRoutes.Add(new MemberRouteDto
            {
                MemberId = stop.PassengerId,
                MemberName = stop.PassengerName,
                EstimatedTimeSeconds = rideTimeSeconds + walkingSeconds + snapshot.WaitSeconds,
                DistanceMeters = rideDistanceMeters + stop.WalkingDistanceMeters,
                RideDistanceMeters = rideDistanceMeters,
                RideTimeSeconds = rideTimeSeconds,
                WaitTimeSeconds = snapshot.WaitSeconds,
                DriverId = driver.Id,
                WalkingDistanceMeters = stop.WalkingDistanceMeters,
                BurdenScore = burdenScore
            });
        }

        var detourPenaltySeconds = Math.Max(0, elapsedSeconds - directRoute.DurationSeconds) * RoutingDefaults.DetourWeight;
        var fairnessPenaltySeconds = ComputeFairnessPenalty(passengerRoutes);
        var stopComplexityPenaltySeconds = orderedStops.Count * RoutingDefaults.StopComplexityWeight;
        var generalizedCost =
            elapsedSeconds +
            walkSecondsTotal * RoutingDefaults.WalkWeight +
            waitSecondsTotal * RoutingDefaults.WaitWeight +
            detourPenaltySeconds +
            fairnessPenaltySeconds +
            stopComplexityPenaltySeconds;

        return new DriverRouteBuildResult(
            new DriverRouteDto
            {
                DriverId = driver.Id,
                DriverName = driver.Name,
                TotalTimeSeconds = elapsedSeconds,
                TotalDistanceMeters = elapsedDistanceMeters,
                DirectTimeSeconds = directRoute.DurationSeconds,
                DirectDistanceMeters = directRoute.DistanceMeters,
                GeneralizedCostSeconds = generalizedCost,
                PassengerIds = orderedStops.Select(stop => stop.PassengerId).ToList(),
                Stops = routeStops
            },
            passengerRoutes,
            new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = generalizedCost,
                TotalDriveSeconds = elapsedSeconds,
                TotalWalkSeconds = walkSecondsTotal,
                TotalWaitSeconds = waitSecondsTotal,
                DetourPenaltySeconds = detourPenaltySeconds,
                FairnessPenaltySeconds = fairnessPenaltySeconds,
                StopComplexityPenaltySeconds = stopComplexityPenaltySeconds
            });
    }

    private async Task<List<PickupStopCandidate>> OrderStopsAsync(
        Member driver,
        IReadOnlyList<PickupStopCandidate> stops,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var remainingStops = stops.ToList();
        var orderedStops = new List<PickupStopCandidate>();
        var current = driver.GetLocation();

        while (remainingStops.Count > 0)
        {
            PickupStopCandidate? bestStop = null;
            var bestDuration = double.MaxValue;

            foreach (var stop in remainingStops)
            {
                var route = await GetRouteAsync(current, stop.StopLocation, driver.TransportMode, trafficSnapshot, ct);
                if (route.DurationSeconds < bestDuration)
                {
                    bestDuration = route.DurationSeconds;
                    bestStop = stop;
                }
            }

            if (bestStop == null)
                break;

            orderedStops.Add(bestStop);
            remainingStops.Remove(bestStop);
            current = bestStop.StopLocation;
        }

        return orderedStops;
    }

    private async Task<PickupStopCandidate> SelectPickupStopAsync(
        Member driver,
        Member passenger,
        Coordinate venueLocation,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var passengerLocation = passenger.GetLocation();
        var doorstepRoute = await GetRouteAsync(driver.GetLocation(), passengerLocation, driver.TransportMode, trafficSnapshot, ct);
        var doorstepToVenue = await GetRouteAsync(passengerLocation, venueLocation, driver.TransportMode, trafficSnapshot, ct);

        var best = new PickupStopCandidate(
            passenger.Id,
            passenger.Name,
            passengerLocation,
            0,
            $"{passenger.Name} (đón tận nơi)",
            doorstepRoute.DurationSeconds + doorstepToVenue.DurationSeconds);

        var driverDistanceToPassenger = driver.GetLocation().DistanceTo(passengerLocation);
        if (driverDistanceToPassenger < 30)
            return best;

        var walkDistance = Math.Min(RoutingDefaults.MaxWalkDistanceMeters, driverDistanceToPassenger * 0.35);
        if (walkDistance < 25)
            return best;

        var walkingStop = MoveToward(passengerLocation, driver.GetLocation(), walkDistance);
        var walkRoute = await GetRouteAsync(driver.GetLocation(), walkingStop, driver.TransportMode, trafficSnapshot, ct);
        var walkToVenue = await GetRouteAsync(walkingStop, venueLocation, driver.TransportMode, trafficSnapshot, ct);
        var walkPenaltySeconds = walkDistance / RoutingDefaults.WalkSpeedMetersPerSecond;
        var walkScore = walkRoute.DurationSeconds + walkToVenue.DurationSeconds + walkPenaltySeconds * RoutingDefaults.WalkWeight;

        return walkScore < best.SortScore
            ? new PickupStopCandidate(
                passenger.Id,
                passenger.Name,
                walkingStop,
                walkDistance,
                $"{passenger.Name} (đi bộ tới điểm đón)",
                walkScore)
            : best;
    }

    private async Task<RouteResult> GetRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct) =>
        await _routeCostProvider.GetExactRouteAsync(
            origin,
            destination,
            mode,
            new RouteCostContext(false, trafficSnapshot.BucketKey),
            ct);

    private static void AggregateBreakdown(RouteScoreBreakdownDto aggregate, RouteScoreBreakdownDto current)
    {
        aggregate.GeneralizedCostSeconds += current.GeneralizedCostSeconds;
        aggregate.TotalDriveSeconds += current.TotalDriveSeconds;
        aggregate.TotalWalkSeconds += current.TotalWalkSeconds;
        aggregate.TotalWaitSeconds += current.TotalWaitSeconds;
        aggregate.DetourPenaltySeconds += current.DetourPenaltySeconds;
        aggregate.FairnessPenaltySeconds += current.FairnessPenaltySeconds;
        aggregate.StopComplexityPenaltySeconds += current.StopComplexityPenaltySeconds;
    }

    private static double CalculateVenueQualityBonusSeconds(Venue venue)
    {
        if (venue.Rating <= 0)
            return 0;

        var reviewWeight = Math.Min(1.0, venue.ReviewCount / 400.0);
        var ratingWeight = Math.Max(0, (venue.Rating - 3.8) / 1.2);
        return Math.Min(RoutingDefaults.QualityBonusCapSeconds, reviewWeight * ratingWeight * RoutingDefaults.QualityBonusCapSeconds);
    }

    private static double ComputeFairnessPenalty(IReadOnlyList<MemberRouteDto> passengerRoutes)
    {
        if (passengerRoutes.Count == 0)
            return 0;

        var burdens = passengerRoutes.Select(route => route.BurdenScore).ToList();
        var average = burdens.Average();
        var variance = burdens.Sum(burden => Math.Pow(burden - average, 2)) / burdens.Count;
        return Math.Sqrt(variance) * RoutingDefaults.FairnessWeight;
    }

    private static double EstimateWaitSeconds(double stopEtaSeconds, double walkingDistanceMeters)
    {
        var walkSeconds = walkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
        return Math.Min(
            120,
            RoutingDefaults.SyncBufferSeconds +
            stopEtaSeconds * RoutingDefaults.WaitEtaFactor +
            walkSeconds * 0.15);
    }

    private static Coordinate MoveToward(Coordinate from, Coordinate to, double distanceMeters)
    {
        var totalDistance = from.DistanceTo(to);
        if (totalDistance <= 0 || distanceMeters <= 0)
            return from;

        var ratio = Math.Min(1.0, distanceMeters / totalDistance);
        return new Coordinate(
            from.Latitude + (to.Latitude - from.Latitude) * ratio,
            from.Longitude + (to.Longitude - from.Longitude) * ratio);
    }

    private sealed record PickupStopCandidate(
        Guid PassengerId,
        string PassengerName,
        Coordinate StopLocation,
        double WalkingDistanceMeters,
        string Label,
        double SortScore);

    private sealed record PickupSnapshot(
        double CumulativeTimeSeconds,
        double CumulativeDistanceMeters,
        double WalkingDistanceMeters,
        double WaitSeconds);

    private sealed record DriverRouteBuildResult(
        DriverRouteDto Route,
        IReadOnlyList<MemberRouteDto> PassengerRoutes,
        RouteScoreBreakdownDto CostBreakdown);
}
