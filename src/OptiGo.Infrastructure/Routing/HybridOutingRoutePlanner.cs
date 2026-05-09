using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Routing;

public class HybridOutingRoutePlanner : IOutingRoutePlanner
{
    private readonly IDriverRouteOptimizer _driverRouteOptimizer;
    private readonly IDriverRouteOptimizer _doorstepRouteOptimizer;
    private readonly bool _enableDoorstepFallback;
    private readonly IRouteCostProvider _routeCostProvider;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;

    public HybridOutingRoutePlanner(
        IDriverRouteOptimizer driverRouteOptimizer,
        IRouteCostProvider routeCostProvider,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
        _driverRouteOptimizer = driverRouteOptimizer;
        _enableDoorstepFallback = driverRouteOptimizer is SharedDestinationRouteOptimizer;
        _doorstepRouteOptimizer = _enableDoorstepFallback
            ? new SharedDestinationRouteOptimizer(
                new DoorstepOnlyStopCandidateGenerator(),
                routeCostProvider)
            : driverRouteOptimizer;
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
            var candidate = await PlanVenueWithAssignmentsAsync(
                session,
                venue,
                trafficSnapshot,
                assignmentSolution,
                _driverRouteOptimizer,
                ct);
            if (IsBetterCandidate(candidate, best))
            {
                best = candidate;
            }

            if (_enableDoorstepFallback &&
                session.Members.Count <= RoutingDefaults.SmallGroupExactMemberLimit)
            {
                var doorstepCandidate = await PlanVenueWithAssignmentsAsync(
                    session,
                    venue,
                    trafficSnapshot,
                    assignmentSolution,
                    _doorstepRouteOptimizer,
                    ct);
                if (IsBetterCandidate(doorstepCandidate, best))
                {
                    best = doorstepCandidate;
                }
            }
        }

        return best ?? await PlanVenueWithAssignmentsAsync(
            session,
            venue,
            trafficSnapshot,
            PickupAssignmentSolution.Empty(session.Members.Where(member => member.CanOfferPickup())),
            _driverRouteOptimizer,
            ct);
    }

    private async Task<CandidateResultDto> PlanVenueWithAssignmentsAsync(
        Session session,
        Venue venue,
        TrafficSnapshot trafficSnapshot,
        PickupAssignmentSolution assignmentSolution,
        IDriverRouteOptimizer routeOptimizer,
        CancellationToken ct)
    {
        var optimizedRoutes = new List<DriverOptimizationResult>();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            IReadOnlyList<Member> passengers = assignmentSolution.PassengersByDriver.TryGetValue(driver.Id, out var assignedPassengers)
                ? assignedPassengers
                : [];

            var optimized = await routeOptimizer.OptimizeAsync(
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

    private static bool IsBetterCandidate(CandidateResultDto candidate, CandidateResultDto? incumbent)
    {
        if (incumbent == null)
            return true;

        if (candidate.IsFeasible != incumbent.IsFeasible)
            return candidate.IsFeasible;

        var candidateCost = RoutingSolutionScorer.CalculateCompositeCost(candidate);
        var incumbentCost = RoutingSolutionScorer.CalculateCompositeCost(incumbent);
        if (candidateCost + 1e-6 < incumbentCost * 0.98)
            return true;

        if (candidateCost <= incumbentCost * 1.02)
        {
            if (candidate.Metrics.MaxMemberBurdenSeconds + 20 < incumbent.Metrics.MaxMemberBurdenSeconds)
                return true;

            if (candidate.Metrics.MaxPassengerTimeSeconds + 30 < incumbent.Metrics.MaxPassengerTimeSeconds)
                return true;

            if (candidate.Metrics.WorstMemberRegretSeconds + 20 < incumbent.Metrics.WorstMemberRegretSeconds)
                return true;

            if (candidate.Metrics.DriverDetourGini + 0.04 < incumbent.Metrics.DriverDetourGini)
                return true;

            if (candidate.Metrics.StdDriverDetourSeconds + 20 < incumbent.Metrics.StdDriverDetourSeconds)
                return true;

            if (candidate.Metrics.MaxDriverDetourSeconds + 30 < incumbent.Metrics.MaxDriverDetourSeconds)
                return true;

            if (candidate.Metrics.MaxWalkingTimeSeconds <= RoutingDefaults.MaxWalkSeconds &&
                candidate.Metrics.SharedStopRate > incumbent.Metrics.SharedStopRate + 0.15)
            {
                return true;
            }
        }

        return candidateCost + 1e-6 < incumbentCost;
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

        var qualityBonusSeconds = RoutingSolutionScorer.CalculateVenueQualityBonusSeconds(venue);
        aggregateBreakdown.VenueQualityBonusSeconds = qualityBonusSeconds;
        aggregateBreakdown.GeneralizedCostSeconds = Math.Max(0, aggregateBreakdown.GeneralizedCostSeconds - qualityBonusSeconds);
        var metrics = RoutingSolutionScorer.BuildMetrics(venue, memberRoutes, driverRoutes);
        var feasibilityIssues = RoutingSolutionScorer.ValidateSolution(session, memberRoutes, driverRoutes, metrics);
        if (feasibilityIssues.Count > 0)
        {
            aggregateBreakdown.GeneralizedCostSeconds += feasibilityIssues.Count * RoutingDefaults.FeasibilityIssuePenaltySeconds;
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
        var limit = passengers.Count <= RoutingDefaults.SmallGroupExactMemberLimit
            ? RoutingDefaults.MaxExactRoutePoolSubsetsPerDriver
            : RoutingDefaults.MaxRoutePoolCandidatesPerDriver * 4;
        ExploreSubsets(0, maxCount, passengers, new List<Member>(), results, limit);
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
        List<List<Member>> results,
        int limit)
    {
        if (results.Count >= limit)
            return;

        if (index >= passengers.Count || remaining == 0)
        {
            results.Add(current.ToList());
            return;
        }

        ExploreSubsets(index + 1, remaining, passengers, current, results, limit);

        current.Add(passengers[index]);
        ExploreSubsets(index + 1, remaining - 1, passengers, current, results, limit);
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
            .OrderByDescending(passenger => ComputeAssignmentRegret(optionsByPassenger[passenger.Id]))
            .ThenBy(passenger => optionsByPassenger[passenger.Id].First().ScoreSeconds)
            .ThenBy(passenger => passenger.JoinedAt)
            .ToList();
        var suffixLowerBounds = BuildAssignmentSuffixLowerBounds(orderedPendingPassengers, optionsByPassenger);

        var results = new List<PickupAssignmentSolution>();
        var exploredStates = 0;
        ExploreAssignments(
            0,
            orderedPendingPassengers,
            drivers,
            optionsByPassenger,
            passengersByDriver,
            assignedPassengerIds,
            0,
            suffixLowerBounds,
            results,
            ref exploredStates);

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
            var optionLimit = passengers.Count + drivers.Count <= RoutingDefaults.SmallGroupExactMemberLimit
                ? drivers.Count
                : Math.Max(3, drivers.Count);
            optionsByPassenger[passenger.Id] = optionsByPassenger[passenger.Id]
                .OrderBy(option => option.ScoreSeconds)
                .Take(optionLimit)
                .ToList();

            if (optionsByPassenger[passenger.Id].Count == 0)
            {
                throw new InvalidOperationException($"Không có tài xế còn ghế cho passenger {passenger.Name}.");
            }
        }

        return optionsByPassenger;
    }

    private static double ComputeAssignmentRegret(IReadOnlyList<AssignmentOption> options)
    {
        if (options.Count == 0)
            return 0;

        if (options.Count == 1)
            return double.PositiveInfinity;

        return options[1].ScoreSeconds - options[0].ScoreSeconds;
    }

    private static double[] BuildAssignmentSuffixLowerBounds(
        IReadOnlyList<Member> orderedPendingPassengers,
        IReadOnlyDictionary<Guid, List<AssignmentOption>> optionsByPassenger)
    {
        var suffix = new double[orderedPendingPassengers.Count + 1];
        for (var index = orderedPendingPassengers.Count - 1; index >= 0; index--)
        {
            var passenger = orderedPendingPassengers[index];
            var best = optionsByPassenger[passenger.Id]
                .Select(option => option.ScoreSeconds)
                .DefaultIfEmpty(0)
                .Min();
            suffix[index] = suffix[index + 1] + best;
        }

        return suffix;
    }

    private static void ExploreAssignments(
        int passengerIndex,
        IReadOnlyList<Member> pendingPassengers,
        IReadOnlyList<Member> drivers,
        IReadOnlyDictionary<Guid, List<AssignmentOption>> optionsByPassenger,
        Dictionary<Guid, List<Member>> passengersByDriver,
        HashSet<Guid> assignedPassengerIds,
        double estimatedCostSeconds,
        IReadOnlyList<double> suffixLowerBounds,
        List<PickupAssignmentSolution> results,
        ref int exploredStates)
    {
        exploredStates++;
        if (exploredStates > RoutingDefaults.MaxExactAssignmentStates)
            return;

        var optimisticCost = estimatedCostSeconds + suffixLowerBounds[passengerIndex];
        if (results.Count >= RoutingDefaults.MaxAssignmentSolutions &&
            optimisticCost >= results[^1].EstimatedCostSeconds)
        {
            return;
        }

        if (passengerIndex >= pendingPassengers.Count)
        {
            var imbalancePenaltySeconds = ComputeLoadImbalancePenalty(drivers, passengersByDriver);
            AddBestAssignmentSolution(results, CreateAssignmentSolution(
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

            var sharedClusterBonusSeconds = driverPassengers.Count(existingPassenger =>
                existingPassenger.GetLocation().DistanceTo(passenger.GetLocation()) <= RoutingDefaults.SharedClusterRadiusMeters) * 140;
            var dynamicLoadPenaltySeconds = driverPassengers.Count * 35 - sharedClusterBonusSeconds;
            var nextCost = estimatedCostSeconds + option.ScoreSeconds + dynamicLoadPenaltySeconds;
            if (results.Count >= RoutingDefaults.MaxAssignmentSolutions &&
                nextCost + suffixLowerBounds[passengerIndex + 1] >= results[^1].EstimatedCostSeconds)
            {
                continue;
            }

            driverPassengers.Add(passenger);
            assignedPassengerIds.Add(passenger.Id);

            ExploreAssignments(
                passengerIndex + 1,
                pendingPassengers,
                drivers,
                optionsByPassenger,
                passengersByDriver,
                assignedPassengerIds,
                nextCost,
                suffixLowerBounds,
                results,
                ref exploredStates);

            assignedPassengerIds.Remove(passenger.Id);
            driverPassengers.RemoveAt(driverPassengers.Count - 1);
        }
    }

    private static void AddBestAssignmentSolution(
        List<PickupAssignmentSolution> results,
        PickupAssignmentSolution candidate)
    {
        var index = results.FindIndex(solution => candidate.EstimatedCostSeconds < solution.EstimatedCostSeconds);
        if (index < 0)
        {
            results.Add(candidate);
        }
        else
        {
            results.Insert(index, candidate);
        }

        if (results.Count > RoutingDefaults.MaxAssignmentSolutions)
        {
            results.RemoveAt(results.Count - 1);
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

    private sealed class DoorstepOnlyStopCandidateGenerator : IStopCandidateGenerator
    {
        public Task<IReadOnlyList<StopCandidate>> GenerateAsync(
            DriverOptimizationInput input,
            CancellationToken ct = default)
        {
            IReadOnlyList<StopCandidate> candidates = input.Passengers
                .Select(passenger => new StopCandidate
                {
                    CandidateId = $"{passenger.Id}:doorstep-only",
                    StopLocation = passenger.GetLocation(),
                    Label = passenger.Name,
                    StopAccessType = "doorstep",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = 0 },
                    AccessPenaltySeconds = 0,
                    RiskPenaltySeconds = 0
                })
                .ToList();

            return Task.FromResult(candidates);
        }
    }
}
