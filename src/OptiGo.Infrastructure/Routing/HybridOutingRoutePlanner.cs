using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Routing;

public class HybridOutingRoutePlanner : IOutingRoutePlanner
{
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
        var trafficSnapshot = _trafficSnapshotProvider.GetCurrentSnapshot();
        CandidateResultDto? best = null;

        var routePoolCandidate = await TryPlanVenueWithRoutePoolAsync(session, venue, trafficSnapshot, ct);
        if (routePoolCandidate != null)
        {
            best = routePoolCandidate;
        }

        var assignmentSolutions = await BuildPickupAssignmentSolutionsAsync(session, venue, trafficSnapshot, ct);

        foreach (var assignmentSolution in assignmentSolutions.OrderBy(solution => solution.EstimatedCostSeconds))
        {
            var candidate = await PlanVenueWithAssignmentsAsync(session, venue, trafficSnapshot, assignmentSolution, ct);
            if (best == null ||
                candidate.ScoreBreakdown.GeneralizedCostSeconds < best.ScoreBreakdown.GeneralizedCostSeconds)
            {
                best = candidate;
            }
        }

        return best ?? await PlanVenueWithAssignmentsAsync(
            session,
            venue,
            trafficSnapshot,
            PickupAssignmentSolution.Empty(session.Members.Where(member => member.CanOfferPickup())),
            ct);
    }

    private async Task<CandidateResultDto> PlanVenueWithAssignmentsAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        PickupAssignmentSolution assignmentSolution,
        CancellationToken ct)
    {
        var optimizedRoutes = new List<DriverOptimizationResult>();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            IReadOnlyList<Member> passengers = assignmentSolution.PassengersByDriver.TryGetValue(driver.Id, out var assignedPassengers)
                ? assignedPassengers
                : [];

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

            optimizedRoutes.Add(optimized);
        }

        return await BuildCandidateFromDriverResultsAsync(
            session,
            venue,
            trafficSnapshot,
            optimizedRoutes,
            assignmentSolution.AssignedPassengerIds,
            ct);
    }

    private async Task<CandidateResultDto> BuildCandidateFromDriverResultsAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        IReadOnlyList<DriverOptimizationResult> optimizedRoutes,
        IReadOnlySet<Guid> assignedPassengerIds,
        CancellationToken ct)
    {
        var driverRoutes = new List<DriverRouteDto>();
        var memberRoutes = new List<MemberRouteDto>();
        var aggregateBreakdown = new RouteScoreBreakdownDto();

        foreach (var optimized in optimizedRoutes)
        {
            driverRoutes.Add(optimized.DriverRoute);
            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = optimized.DriverRoute.DriverId,
                MemberName = optimized.DriverRoute.DriverName,
                EstimatedTimeSeconds = optimized.DriverRoute.TotalTimeSeconds,
                DistanceMeters = optimized.DriverRoute.TotalDistanceMeters,
                RideDistanceMeters = optimized.DriverRoute.TotalDistanceMeters,
                RideTimeSeconds = optimized.DriverRoute.TotalTimeSeconds,
                DriverId = optimized.DriverRoute.DriverId,
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
        var metrics = BuildSolutionMetrics(venue, memberRoutes, driverRoutes);
        var feasibilityIssues = ValidateSolution(session, memberRoutes, driverRoutes, metrics);
        if (feasibilityIssues.Count > 0)
        {
            aggregateBreakdown.GeneralizedCostSeconds += feasibilityIssues.Count * 600;
        }

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
            IsFeasible = feasibilityIssues.Count == 0,
            FeasibilityIssues = feasibilityIssues,
            Metrics = metrics,
            OptimizationReason = BuildOptimizationReason(venue, metrics),
            TradeOffSummary = BuildTradeOffSummary(metrics),
            ScoreBreakdown = aggregateBreakdown,
            MemberRoutes = memberRoutes.OrderBy(route => route.MemberName).ToList(),
            DriverRoutes = driverRoutes.OrderBy(route => route.DriverName).ToList()
        };
    }

    private async Task<CandidateResultDto?> TryPlanVenueWithRoutePoolAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var drivers = session.Members
            .Where(member => member.CanOfferPickup())
            .OrderBy(member => member.JoinedAt)
            .ToList();
        var pickupPassengers = session.Members
            .Where(member => member.NeedsPickup())
            .OrderBy(member => member.JoinedAt)
            .ToList();

        if (drivers.Count == 0 || pickupPassengers.Count == 0)
            return null;

        var routePool = await GenerateRoutePoolAsync(session, venue, trafficSnapshot, drivers, pickupPassengers, ct);
        if (routePool.Count == 0)
            return null;

        var selected = SolveRoutePool(drivers, pickupPassengers, routePool);
        if (selected == null)
            return null;

        return await BuildCandidateFromDriverResultsAsync(
            session,
            venue,
            trafficSnapshot,
            selected.Candidates.Select(candidate => candidate.Result).ToList(),
            selected.CoveredPassengerIds,
            ct);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<RoutePoolCandidate>>> GenerateRoutePoolAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        IReadOnlyList<Member> drivers,
        IReadOnlyList<Member> pickupPassengers,
        CancellationToken ct)
    {
        var membersById = session.Members.ToDictionary(member => member.Id);
        var acceptedByDriver = session.PickupRequests
            .Where(request => request.IsAccepted() && request.AcceptedDriverId.HasValue)
            .GroupBy(request => request.AcceptedDriverId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(request => membersById.TryGetValue(request.PassengerId, out var passenger) ? passenger : null)
                    .Where(passenger => passenger != null)
                    .Select(passenger => passenger!)
                    .ToList());
        var lockedPassengerIds = acceptedByDriver.Values
            .SelectMany(passengers => passengers.Select(passenger => passenger.Id))
            .ToHashSet();
        var pendingPassengers = pickupPassengers
            .Where(passenger => !lockedPassengerIds.Contains(passenger.Id))
            .ToList();
        var routePool = new Dictionary<Guid, IReadOnlyList<RoutePoolCandidate>>();

        foreach (var driver in drivers)
        {
            acceptedByDriver.TryGetValue(driver.Id, out var requiredPassengers);
            requiredPassengers ??= [];

            if (requiredPassengers.Count > driver.GetSeatCapacity())
                return new Dictionary<Guid, IReadOnlyList<RoutePoolCandidate>>();

            var remainingCapacity = driver.GetSeatCapacity() - requiredPassengers.Count;
            var orderedPending = pendingPassengers
                .OrderBy(passenger => passenger.GetLocation().DistanceTo(driver.GetLocation()))
                .ToList();
            var subsets = BuildPassengerSubsets(orderedPending, remainingCapacity);
            var candidates = new List<RoutePoolCandidate>();

            foreach (var subset in subsets)
            {
                var passengers = requiredPassengers.Concat(subset).ToList();
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

                var coveredPassengerIds = passengers
                    .Select(passenger => passenger.Id)
                    .ToHashSet();
                var feasibilityPenalty = ComputeRoutePoolFeasibilityPenalty(driver, optimized);
                var routeCost =
                    optimized.CostBreakdown.GeneralizedCostSeconds +
                    feasibilityPenalty;

                candidates.Add(new RoutePoolCandidate(
                    driver.Id,
                    coveredPassengerIds,
                    optimized,
                    routeCost));
            }

            routePool[driver.Id] = candidates
                .OrderBy(candidate => candidate.CostSeconds)
                .Take(RoutingDefaults.MaxRoutePoolCandidatesPerDriver)
                .ToList();
        }

        return routePool;
    }

    private static List<List<Member>> BuildPassengerSubsets(
        IReadOnlyList<Member> passengers,
        int maxCount)
    {
        var results = new List<List<Member>>();
        ExploreSubsets(0, maxCount, passengers, new List<Member>(), results);
        return results
            .OrderBy(subset => subset.Count)
            .ThenBy(subset => string.Join("|", subset.Select(passenger => passenger.Id)))
            .ToList();
    }

    private static void ExploreSubsets(
        int index,
        int remaining,
        IReadOnlyList<Member> passengers,
        List<Member> current,
        List<List<Member>> results)
    {
        if (results.Count >= RoutingDefaults.MaxRoutePoolCandidatesPerDriver * 4)
            return;

        if (index >= passengers.Count || remaining == 0)
        {
            results.Add(current.ToList());
            return;
        }

        ExploreSubsets(index + 1, remaining, passengers, current, results);

        current.Add(passengers[index]);
        ExploreSubsets(index + 1, remaining - 1, passengers, current, results);
        current.RemoveAt(current.Count - 1);
    }

    private static RoutePoolSelection? SolveRoutePool(
        IReadOnlyList<Member> drivers,
        IReadOnlyList<Member> pickupPassengers,
        IReadOnlyDictionary<Guid, IReadOnlyList<RoutePoolCandidate>> routePool)
    {
        var targetPassengerIds = pickupPassengers.Select(passenger => passenger.Id).ToHashSet();
        RoutePoolSelection? best = null;

        ExploreRoutePool(
            0,
            drivers,
            targetPassengerIds,
            routePool,
            new HashSet<Guid>(),
            new List<RoutePoolCandidate>(),
            0,
            ref best);

        return best;
    }

    private static void ExploreRoutePool(
        int driverIndex,
        IReadOnlyList<Member> drivers,
        IReadOnlySet<Guid> targetPassengerIds,
        IReadOnlyDictionary<Guid, IReadOnlyList<RoutePoolCandidate>> routePool,
        HashSet<Guid> coveredPassengerIds,
        List<RoutePoolCandidate> selectedCandidates,
        double costSeconds,
        ref RoutePoolSelection? best)
    {
        if (best != null && costSeconds >= best.CostSeconds)
            return;

        if (driverIndex >= drivers.Count)
        {
            if (!coveredPassengerIds.SetEquals(targetPassengerIds))
                return;

            best = new RoutePoolSelection(
                selectedCandidates.ToList(),
                coveredPassengerIds.ToHashSet(),
                costSeconds + ComputeSelectedRoutePoolImbalancePenalty(drivers, selectedCandidates));
            return;
        }

        var driver = drivers[driverIndex];
        if (!routePool.TryGetValue(driver.Id, out var candidates) || candidates.Count == 0)
            return;

        foreach (var candidate in candidates)
        {
            if (candidate.CoveredPassengerIds.Any(coveredPassengerIds.Contains))
                continue;

            foreach (var passengerId in candidate.CoveredPassengerIds)
            {
                coveredPassengerIds.Add(passengerId);
            }

            selectedCandidates.Add(candidate);
            ExploreRoutePool(
                driverIndex + 1,
                drivers,
                targetPassengerIds,
                routePool,
                coveredPassengerIds,
                selectedCandidates,
                costSeconds + candidate.CostSeconds,
                ref best);
            selectedCandidates.RemoveAt(selectedCandidates.Count - 1);

            foreach (var passengerId in candidate.CoveredPassengerIds)
            {
                coveredPassengerIds.Remove(passengerId);
            }
        }
    }

    private static double ComputeRoutePoolFeasibilityPenalty(Member driver, DriverOptimizationResult optimized)
    {
        var detourSeconds = Math.Max(
            0,
            optimized.DriverRoute.TotalTimeSeconds - optimized.DriverRoute.DirectTimeSeconds);
        var detourPenalty = Math.Max(0, detourSeconds - RoutingDefaults.MaxDriverDetourSeconds) * 2.0;
        var capacityPenalty = Math.Max(0, optimized.DriverRoute.PassengerIds.Count - driver.GetSeatCapacity()) * 900;
        var passengerPenalty = optimized.PassengerRoutes.Sum(route =>
            Math.Max(0, route.EstimatedTimeSeconds - RoutingDefaults.MaxPassengerTotalTravelSeconds) * 1.5 +
            Math.Max(0, route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond - RoutingDefaults.MaxWalkSeconds) * 2.0);

        return detourPenalty + capacityPenalty + passengerPenalty;
    }

    private static double ComputeSelectedRoutePoolImbalancePenalty(
        IReadOnlyList<Member> drivers,
        IReadOnlyList<RoutePoolCandidate> selectedCandidates)
    {
        if (drivers.Count <= 1)
            return 0;

        var selectedByDriver = selectedCandidates.ToDictionary(candidate => candidate.DriverId);
        var loadRatios = drivers
            .Where(driver => driver.GetSeatCapacity() > 0)
            .Select(driver =>
                selectedByDriver.TryGetValue(driver.Id, out var candidate)
                    ? candidate.CoveredPassengerIds.Count / (double)driver.GetSeatCapacity()
                    : 0)
            .ToList();

        return loadRatios.Count <= 1 ? 0 : (loadRatios.Max() - loadRatios.Min()) * 90;
    }

    private async Task<IReadOnlyList<PickupAssignmentSolution>> BuildPickupAssignmentSolutionsAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var drivers = session.Members
            .Where(member => member.CanOfferPickup())
            .OrderBy(member => member.JoinedAt)
            .ToList();
        var pickupPassengers = session.Members
            .Where(member => member.NeedsPickup())
            .OrderBy(member => member.JoinedAt)
            .ToList();
        var passengersByDriver = drivers.ToDictionary(driver => driver.Id, _ => new List<Member>());
        var assignedPassengerIds = new HashSet<Guid>();
        var membersById = session.Members.ToDictionary(member => member.Id);

        foreach (var request in session.PickupRequests.Where(request => request.IsAccepted()))
        {
            if (!request.AcceptedDriverId.HasValue ||
                !membersById.TryGetValue(request.PassengerId, out var passenger) ||
                !membersById.TryGetValue(request.AcceptedDriverId.Value, out var driver) ||
                !driver.CanOfferPickup())
            {
                continue;
            }

            if (passengersByDriver[driver.Id].Count >= driver.GetSeatCapacity())
            {
                throw new InvalidOperationException($"Driver {driver.Name} exceeds vehicle capacity.");
            }

            passengersByDriver[driver.Id].Add(passenger);
            assignedPassengerIds.Add(passenger.Id);
        }

        var pendingPassengers = pickupPassengers
            .Where(passenger => !assignedPassengerIds.Contains(passenger.Id))
            .ToList();

        if (pendingPassengers.Count == 0)
        {
            return [CreateAssignmentSolution(passengersByDriver, assignedPassengerIds, 0)];
        }

        if (drivers.Count == 0)
        {
            throw new InvalidOperationException("Không có tài xế nào có thể đón passenger.");
        }

        var remainingSeats = drivers.Sum(driver =>
            Math.Max(0, driver.GetSeatCapacity() - passengersByDriver[driver.Id].Count));
        if (remainingSeats < pendingPassengers.Count)
        {
            throw new InvalidOperationException("Không đủ số ghế trống để phân bổ tất cả passenger cần đón.");
        }

        var optionsByPassenger = await BuildAssignmentOptionsAsync(
            drivers,
            pendingPassengers,
            passengersByDriver,
            venue,
            trafficSnapshot,
            ct);

        var orderedPendingPassengers = pendingPassengers
            .OrderBy(passenger => optionsByPassenger[passenger.Id].Count)
            .ThenBy(passenger => passenger.JoinedAt)
            .ToList();

        var results = new List<PickupAssignmentSolution>();
        ExploreAssignments(
            0,
            orderedPendingPassengers,
            drivers,
            optionsByPassenger,
            passengersByDriver,
            assignedPassengerIds,
            0,
            results);

        if (results.Count == 0)
        {
            throw new InvalidOperationException("Không tìm được phương án phân bổ passenger-driver hợp lệ.");
        }

        return results
            .OrderBy(solution => solution.EstimatedCostSeconds)
            .Take(RoutingDefaults.MaxAssignmentSolutions)
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, List<AssignmentOption>>> BuildAssignmentOptionsAsync(
        IReadOnlyList<Member> drivers,
        IReadOnlyList<Member> passengers,
        IReadOnlyDictionary<Guid, List<Member>> currentPassengersByDriver,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        CancellationToken ct)
    {
        var optionsByPassenger = passengers.ToDictionary(passenger => passenger.Id, _ => new List<AssignmentOption>());
        var context = new RouteCostContext(false, trafficSnapshot.BucketKey);
        var passengerLocations = passengers.Select(passenger => passenger.GetLocation()).ToList();
        var venueLocation = venue.GetLocation();

        foreach (var modeGroup in drivers.GroupBy(driver => driver.TransportMode))
        {
            var groupedDrivers = modeGroup.ToList();
            var driverLocations = groupedDrivers.Select(driver => driver.GetLocation()).ToList();
            var driverToPassenger = await _routeCostProvider.GetEstimatedMatrixAsync(
                driverLocations,
                passengerLocations,
                modeGroup.Key,
                context,
                ct);
            var passengerToVenue = await _routeCostProvider.GetEstimatedMatrixAsync(
                passengerLocations,
                [venueLocation],
                modeGroup.Key,
                context,
                ct);
            var driverToVenue = await _routeCostProvider.GetEstimatedMatrixAsync(
                driverLocations,
                [venueLocation],
                modeGroup.Key,
                context,
                ct);

            for (var driverIndex = 0; driverIndex < groupedDrivers.Count; driverIndex++)
            {
                var driver = groupedDrivers[driverIndex];
                var currentLoad = currentPassengersByDriver[driver.Id].Count;
                if (currentLoad >= driver.GetSeatCapacity())
                    continue;

                for (var passengerIndex = 0; passengerIndex < passengers.Count; passengerIndex++)
                {
                    var passenger = passengers[passengerIndex];
                    var detourLowerBoundSeconds = Math.Max(
                        0,
                        driverToPassenger.Durations[driverIndex, passengerIndex] +
                        passengerToVenue.Durations[passengerIndex, 0] -
                        driverToVenue.Durations[driverIndex, 0]);
                    var loadPressureSeconds = currentLoad * 45;
                    var overDetourPenaltySeconds = Math.Max(
                        0,
                        detourLowerBoundSeconds - RoutingDefaults.MaxDriverDetourSeconds) * 2.0;
                    var scoreSeconds =
                        detourLowerBoundSeconds +
                        loadPressureSeconds +
                        overDetourPenaltySeconds;

                    optionsByPassenger[passenger.Id].Add(new AssignmentOption(
                        driver,
                        detourLowerBoundSeconds,
                        scoreSeconds));
                }
            }
        }

        foreach (var passenger in passengers)
        {
            optionsByPassenger[passenger.Id] = optionsByPassenger[passenger.Id]
                .OrderBy(option => option.ScoreSeconds)
                .Take(Math.Max(3, drivers.Count))
                .ToList();

            if (optionsByPassenger[passenger.Id].Count == 0)
            {
                throw new InvalidOperationException($"Không có tài xế còn ghế cho passenger {passenger.Name}.");
            }
        }

        return optionsByPassenger;
    }

    private static void ExploreAssignments(
        int passengerIndex,
        IReadOnlyList<Member> pendingPassengers,
        IReadOnlyList<Member> drivers,
        IReadOnlyDictionary<Guid, List<AssignmentOption>> optionsByPassenger,
        Dictionary<Guid, List<Member>> passengersByDriver,
        HashSet<Guid> assignedPassengerIds,
        double estimatedCostSeconds,
        List<PickupAssignmentSolution> results)
    {
        if (results.Count >= RoutingDefaults.MaxAssignmentSolutions)
            return;

        if (passengerIndex >= pendingPassengers.Count)
        {
            var imbalancePenaltySeconds = ComputeLoadImbalancePenalty(drivers, passengersByDriver);
            results.Add(CreateAssignmentSolution(
                passengersByDriver,
                assignedPassengerIds,
                estimatedCostSeconds + imbalancePenaltySeconds));
            return;
        }

        var passenger = pendingPassengers[passengerIndex];
        foreach (var option in optionsByPassenger[passenger.Id])
        {
            var driverPassengers = passengersByDriver[option.Driver.Id];
            if (driverPassengers.Count >= option.Driver.GetSeatCapacity())
                continue;

            driverPassengers.Add(passenger);
            assignedPassengerIds.Add(passenger.Id);

            ExploreAssignments(
                passengerIndex + 1,
                pendingPassengers,
                drivers,
                optionsByPassenger,
                passengersByDriver,
                assignedPassengerIds,
                estimatedCostSeconds + option.ScoreSeconds,
                results);

            assignedPassengerIds.Remove(passenger.Id);
            driverPassengers.RemoveAt(driverPassengers.Count - 1);
        }
    }

    private static double ComputeLoadImbalancePenalty(
        IReadOnlyList<Member> drivers,
        IReadOnlyDictionary<Guid, List<Member>> passengersByDriver)
    {
        if (drivers.Count <= 1)
            return 0;

        var loadRatios = drivers
            .Where(driver => driver.GetSeatCapacity() > 0)
            .Select(driver => passengersByDriver[driver.Id].Count / (double)driver.GetSeatCapacity())
            .ToList();

        if (loadRatios.Count <= 1)
            return 0;

        return (loadRatios.Max() - loadRatios.Min()) * 90;
    }

    private static PickupAssignmentSolution CreateAssignmentSolution(
        IReadOnlyDictionary<Guid, List<Member>> passengersByDriver,
        IReadOnlySet<Guid> assignedPassengerIds,
        double estimatedCostSeconds) =>
        new(
            passengersByDriver.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<Member>)entry.Value.ToList()),
            assignedPassengerIds.ToHashSet(),
            estimatedCostSeconds);

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

    private static SolutionMetricsDto BuildSolutionMetrics(
        Venue venue,
        IReadOnlyList<MemberRouteDto> memberRoutes,
        IReadOnlyList<DriverRouteDto> driverRoutes)
    {
        var passengerRoutes = memberRoutes
            .Where(route => route.DriverId.HasValue && route.DriverId.Value != route.MemberId)
            .ToList();
        var passengerTimes = passengerRoutes.Select(route => route.EstimatedTimeSeconds).ToList();
        var averagePassengerTime = passengerTimes.Count > 0 ? passengerTimes.Average() : 0;
        var passengerVariance = passengerTimes.Count > 0
            ? passengerTimes.Sum(time => Math.Pow(time - averagePassengerTime, 2)) / passengerTimes.Count
            : 0;
        var driverDetours = driverRoutes
            .Select(route => Math.Max(0, route.TotalTimeSeconds - route.DirectTimeSeconds))
            .ToList();

        return new SolutionMetricsDto
        {
            TotalGroupTimeSeconds = memberRoutes.Sum(route => route.EstimatedTimeSeconds),
            MaxPassengerTimeSeconds = passengerTimes.DefaultIfEmpty(0).Max(),
            StdPassengerTimeSeconds = Math.Sqrt(passengerVariance),
            TotalWalkingTimeSeconds = memberRoutes.Sum(route => route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond),
            MaxWalkingTimeSeconds = memberRoutes
                .Select(route => route.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond)
                .DefaultIfEmpty(0)
                .Max(),
            MaxDriverDetourSeconds = driverDetours.DefaultIfEmpty(0).Max(),
            TotalDriverDetourSeconds = driverDetours.Sum(),
            VenueRating = venue.Rating,
            StopCount = driverRoutes.Sum(route => route.Stops.Count(stop => stop.StopType.StartsWith("pickup", StringComparison.OrdinalIgnoreCase)))
        };
    }

    private static List<string> ValidateSolution(
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

    private static string BuildOptimizationReason(Venue venue, SolutionMetricsDto metrics)
    {
        var ratingText = venue.Rating > 0 ? $"rating {venue.Rating:0.0}" : "rating chưa rõ";
        return $"Tối ưu trên route thật: tổng nhóm {metrics.TotalGroupTimeSeconds / 60:0} phút, detour max {metrics.MaxDriverDetourSeconds / 60:0} phút, {ratingText}.";
    }

    private static string BuildTradeOffSummary(SolutionMetricsDto metrics)
    {
        if (metrics.StopCount == 0)
            return "Không cần điểm đón trung gian.";

        return $"{metrics.StopCount} điểm đón, đi bộ tối đa {metrics.MaxWalkingTimeSeconds / 60:0} phút, passenger lâu nhất {metrics.MaxPassengerTimeSeconds / 60:0} phút.";
    }

    private static double CalculateVenueQualityBonusSeconds(Venue venue)
    {
        if (venue.Rating <= 0)
            return 0;

        var reviewWeight = Math.Min(1.0, venue.ReviewCount / 400.0);
        var ratingWeight = Math.Max(0, (venue.Rating - 3.8) / 1.2);
        return Math.Min(RoutingDefaults.QualityBonusCapSeconds, reviewWeight * ratingWeight * RoutingDefaults.QualityBonusCapSeconds);
    }

    private sealed record AssignmentOption(
        Member Driver,
        double DetourLowerBoundSeconds,
        double ScoreSeconds);

    private sealed record PickupAssignmentSolution(
        IReadOnlyDictionary<Guid, IReadOnlyList<Member>> PassengersByDriver,
        IReadOnlySet<Guid> AssignedPassengerIds,
        double EstimatedCostSeconds)
    {
        public static PickupAssignmentSolution Empty(IEnumerable<Member> drivers) =>
            new(
                drivers.ToDictionary(driver => driver.Id, _ => (IReadOnlyList<Member>)Array.Empty<Member>()),
                new HashSet<Guid>(),
                0);
    }

    private sealed record RoutePoolCandidate(
        Guid DriverId,
        IReadOnlySet<Guid> CoveredPassengerIds,
        DriverOptimizationResult Result,
        double CostSeconds);

    private sealed record RoutePoolSelection(
        IReadOnlyList<RoutePoolCandidate> Candidates,
        IReadOnlySet<Guid> CoveredPassengerIds,
        double CostSeconds);
}
