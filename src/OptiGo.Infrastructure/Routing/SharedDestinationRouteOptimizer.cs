using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class SharedDestinationRouteOptimizer : IDriverRouteOptimizer
{
    private const int ExactPermutationThreshold = 7;
    private const int MaxCoveringSolutions = 256;

    private readonly IStopCandidateGenerator _stopCandidateGenerator;
    private readonly IRouteCostProvider _routeCostProvider;

    public SharedDestinationRouteOptimizer(
        IStopCandidateGenerator stopCandidateGenerator,
        IRouteCostProvider routeCostProvider)
    {
        _stopCandidateGenerator = stopCandidateGenerator;
        _routeCostProvider = routeCostProvider;
    }

    public async Task<DriverOptimizationResult> OptimizeAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default)
    {
        if (input.Passengers.Count == 0)
        {
            return await BuildDirectDriverResultAsync(input, ct);
        }

        var candidates = await _stopCandidateGenerator.GenerateAsync(input, ct);
        var optionsByPassenger = input.Passengers.ToDictionary(
            passenger => passenger.Id,
            passenger => candidates
                .Where(candidate => candidate.PassengerIds.Contains(passenger.Id))
                .ToList());

        var coveringSolutions = BuildCoveringSolutions(input.Passengers.Select(passenger => passenger.Id).ToList(), optionsByPassenger);
        if (coveringSolutions.Count == 0)
        {
            coveringSolutions.Add(input.Passengers.Select(passenger => candidates.First(candidate =>
                candidate.PassengerIds.Count == 1 &&
                candidate.PassengerIds[0] == passenger.Id &&
                candidate.StopAccessType == "doorstep")).ToList());
        }

        DriverOptimizationResult? best = null;
        foreach (var chosenStops in coveringSolutions)
        {
            var optimizedStops = await OptimizeStopOrderingAsync(input, chosenStops, ct);
            var evaluated = await EvaluateRouteAsync(input, optimizedStops, ct);

            if (best == null || evaluated.CostBreakdown.GeneralizedCostSeconds < best.CostBreakdown.GeneralizedCostSeconds)
            {
                best = evaluated;
            }
        }

        return best ?? await BuildDirectDriverResultAsync(input, ct);
    }

    private List<List<StopCandidate>> BuildCoveringSolutions(
        IReadOnlyList<Guid> passengerIds,
        IReadOnlyDictionary<Guid, List<StopCandidate>> optionsByPassenger)
    {
        var results = new List<List<StopCandidate>>();
        var visitedKeys = new HashSet<string>(StringComparer.Ordinal);
        Explore(passengerIds, optionsByPassenger, new HashSet<Guid>(), new List<StopCandidate>(), results, visitedKeys);
        return results;
    }

    private void Explore(
        IReadOnlyList<Guid> passengerIds,
        IReadOnlyDictionary<Guid, List<StopCandidate>> optionsByPassenger,
        HashSet<Guid> covered,
        List<StopCandidate> chosen,
        List<List<StopCandidate>> results,
        HashSet<string> visitedKeys)
    {
        if (results.Count >= MaxCoveringSolutions)
            return;

        if (covered.Count == passengerIds.Count)
        {
            var key = string.Join("|", chosen.Select(candidate => candidate.CandidateId).OrderBy(value => value, StringComparer.Ordinal));
            if (visitedKeys.Add(key))
            {
                results.Add(chosen.ToList());
            }

            return;
        }

        var nextPassengerId = passengerIds.First(passengerId => !covered.Contains(passengerId));
        foreach (var option in optionsByPassenger[nextPassengerId]
                     .OrderByDescending(candidate => candidate.PassengerIds.Count)
                     .ThenBy(candidate => candidate.AccessPenaltySeconds))
        {
            if (option.PassengerIds.Any(covered.Contains))
                continue;

            foreach (var passengerId in option.PassengerIds)
            {
                covered.Add(passengerId);
            }

            chosen.Add(option);
            Explore(passengerIds, optionsByPassenger, covered, chosen, results, visitedKeys);
            chosen.RemoveAt(chosen.Count - 1);

            foreach (var passengerId in option.PassengerIds)
            {
                covered.Remove(passengerId);
            }
        }
    }

    private async Task<List<StopCandidate>> OptimizeStopOrderingAsync(
        DriverOptimizationInput input,
        IReadOnlyList<StopCandidate> chosenStops,
        CancellationToken ct)
    {
        if (chosenStops.Count <= 1)
            return chosenStops.ToList();

        if (chosenStops.Count <= ExactPermutationThreshold)
        {
            var bestOrder = chosenStops.ToList();
            var bestCost = double.MaxValue;
            await EvaluatePermutationsAsync(input, chosenStops.ToList(), 0, ct, async order =>
            {
                var cost = await EstimateDriverCostAsync(input, order, ct);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestOrder = order.ToList();
                }
            });

            return bestOrder;
        }

        var ordered = await BuildCheapestInsertionOrderAsync(input, chosenStops.ToList(), ct);
        return await ImproveWithRelocateAndTwoOptAsync(input, ordered, ct);
    }

    private async Task EvaluatePermutationsAsync(
        DriverOptimizationInput input,
        List<StopCandidate> items,
        int index,
        CancellationToken ct,
        Func<List<StopCandidate>, Task> onPermutation)
    {
        if (index >= items.Count)
        {
            await onPermutation(items);
            return;
        }

        for (var i = index; i < items.Count; i++)
        {
            (items[index], items[i]) = (items[i], items[index]);
            await EvaluatePermutationsAsync(input, items, index + 1, ct, onPermutation);
            (items[index], items[i]) = (items[i], items[index]);
        }
    }

    private async Task<List<StopCandidate>> BuildCheapestInsertionOrderAsync(
        DriverOptimizationInput input,
        List<StopCandidate> remaining,
        CancellationToken ct)
    {
        var ordered = new List<StopCandidate>();
        StopCandidate? seed = null;
        var bestSeedCost = double.MaxValue;

        foreach (var candidate in remaining)
        {
            var driverLeg = await GetRouteAsync(input.Driver.GetLocation(), candidate.StopLocation, input.Driver.TransportMode, input, ct);
            var venueLeg = await GetRouteAsync(candidate.StopLocation, input.Venue.GetLocation(), input.Driver.TransportMode, input, ct);
            var candidateCost = driverLeg.DurationSeconds + venueLeg.DurationSeconds + candidate.AccessPenaltySeconds;

            if (candidateCost < bestSeedCost)
            {
                bestSeedCost = candidateCost;
                seed = candidate;
            }
        }

        if (seed == null)
            return remaining;

        ordered.Add(seed);
        remaining.Remove(seed);

        while (remaining.Count > 0)
        {
            StopCandidate? bestCandidate = null;
            List<StopCandidate>? bestOrder = null;
            var bestCost = double.MaxValue;

            foreach (var candidate in remaining)
            {
                for (var position = 0; position <= ordered.Count; position++)
                {
                    var proposal = ordered.ToList();
                    proposal.Insert(position, candidate);
                    var proposalCost = await EstimateDriverCostAsync(input, proposal, ct);
                    if (proposalCost < bestCost)
                    {
                        bestCost = proposalCost;
                        bestCandidate = candidate;
                        bestOrder = proposal;
                    }
                }
            }

            if (bestCandidate == null || bestOrder == null)
                break;

            ordered = bestOrder;
            remaining.Remove(bestCandidate);
        }

        return ordered;
    }

    private async Task<List<StopCandidate>> ImproveWithRelocateAndTwoOptAsync(
        DriverOptimizationInput input,
        List<StopCandidate> ordered,
        CancellationToken ct)
    {
        var improved = true;
        var best = ordered.ToList();
        var bestCost = await EstimateDriverCostAsync(input, best, ct);

        while (improved)
        {
            improved = false;

            for (var i = 0; i < best.Count; i++)
            {
                for (var j = i + 1; j < best.Count; j++)
                {
                    var swapped = best.ToList();
                    (swapped[i], swapped[j]) = (swapped[j], swapped[i]);
                    var swappedCost = await EstimateDriverCostAsync(input, swapped, ct);
                    if (swappedCost + 1e-6 < bestCost)
                    {
                        best = swapped;
                        bestCost = swappedCost;
                        improved = true;
                    }
                }
            }

            for (var start = 0; start < best.Count - 1; start++)
            {
                for (var end = start + 1; end < best.Count; end++)
                {
                    var reversed = best.ToList();
                    reversed.Reverse(start, end - start + 1);
                    var reversedCost = await EstimateDriverCostAsync(input, reversed, ct);
                    if (reversedCost + 1e-6 < bestCost)
                    {
                        best = reversed;
                        bestCost = reversedCost;
                        improved = true;
                    }
                }
            }
        }

        return best;
    }

    private async Task<double> EstimateDriverCostAsync(
        DriverOptimizationInput input,
        IReadOnlyList<StopCandidate> orderedStops,
        CancellationToken ct)
    {
        var current = input.Driver.GetLocation();
        double elapsedSeconds = 0;

        foreach (var stop in orderedStops)
        {
            var leg = await GetRouteAsync(current, stop.StopLocation, input.Driver.TransportMode, input, ct);
            elapsedSeconds += leg.DurationSeconds + stop.AccessPenaltySeconds;
            current = stop.StopLocation;
        }

        var venueLeg = await GetRouteAsync(current, input.Venue.GetLocation(), input.Driver.TransportMode, input, ct);
        elapsedSeconds += venueLeg.DurationSeconds;
        return elapsedSeconds + orderedStops.Count * RoutingDefaults.StopComplexityWeight;
    }

    private async Task<DriverOptimizationResult> EvaluateRouteAsync(
        DriverOptimizationInput input,
        IReadOnlyList<StopCandidate> orderedStops,
        CancellationToken ct)
    {
        var driverLocation = input.Driver.GetLocation();
        var venueLocation = input.Venue.GetLocation();
        var directRoute = await GetRouteAsync(driverLocation, venueLocation, input.Driver.TransportMode, input, ct);

        var routeStops = new List<RouteStopDto>
        {
            new()
            {
                Sequence = 0,
                StopType = "driver_origin",
                Label = input.Driver.Name,
                Latitude = driverLocation.Latitude,
                Longitude = driverLocation.Longitude,
                EtaSeconds = 0,
                DistanceFromPreviousMeters = 0,
                CumulativeDistanceMeters = 0,
                CumulativeTimeSeconds = 0,
                StopAccessType = "origin"
            }
        };

        var pickupSnapshots = new Dictionary<Guid, PickupSnapshot>();
        var current = driverLocation;
        double elapsedSeconds = 0;
        double elapsedDistanceMeters = 0;
        double walkSecondsTotal = 0;
        double waitSecondsTotal = 0;
        double riskPenaltySeconds = 0;

        foreach (var stop in orderedStops)
        {
            var leg = await GetRouteAsync(current, stop.StopLocation, input.Driver.TransportMode, input, ct);
            elapsedSeconds += leg.DurationSeconds;
            elapsedDistanceMeters += leg.DistanceMeters;
            current = stop.StopLocation;
            riskPenaltySeconds += stop.RiskPenaltySeconds;

            var maxWalkingMeters = stop.WalkingDistancesMeters.Count == 0
                ? 0
                : stop.WalkingDistancesMeters.Values.Max();
            var waitSeconds = EstimateWaitSeconds(elapsedSeconds, maxWalkingMeters);

            routeStops.Add(new RouteStopDto
            {
                Sequence = routeStops.Count,
                StopType = stop.IsMergedStop
                    ? "pickup_merged"
                    : stop.WalkingDistancesMeters.Values.Any(distance => distance > 0)
                        ? "pickup_meetpoint"
                        : "pickup",
                Label = stop.Label,
                Latitude = stop.StopLocation.Latitude,
                Longitude = stop.StopLocation.Longitude,
                EtaSeconds = elapsedSeconds,
                DistanceFromPreviousMeters = leg.DistanceMeters,
                CumulativeDistanceMeters = elapsedDistanceMeters,
                CumulativeTimeSeconds = elapsedSeconds,
                WalkingDistanceMeters = maxWalkingMeters,
                WaitSeconds = waitSeconds,
                StopAccessType = stop.StopAccessType,
                IsMergedStop = stop.IsMergedStop,
                PassengerIds = stop.PassengerIds.ToList()
            });

            foreach (var passengerId in stop.PassengerIds)
            {
                var walkingMeters = stop.WalkingDistancesMeters.TryGetValue(passengerId, out var value) ? value : 0;
                var passengerWait = EstimateWaitSeconds(elapsedSeconds, walkingMeters);
                pickupSnapshots[passengerId] = new PickupSnapshot(
                    elapsedSeconds,
                    elapsedDistanceMeters,
                    walkingMeters,
                    passengerWait,
                    stop.RiskPenaltySeconds / Math.Max(1, stop.PassengerIds.Count));
                walkSecondsTotal += walkingMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
                waitSecondsTotal += passengerWait;
            }
        }

        var venueLeg = await GetRouteAsync(current, venueLocation, input.Driver.TransportMode, input, ct);
        elapsedSeconds += venueLeg.DurationSeconds;
        elapsedDistanceMeters += venueLeg.DistanceMeters;
        routeStops.Add(new RouteStopDto
        {
            Sequence = routeStops.Count,
            StopType = "destination",
            Label = input.Venue.Name,
            Latitude = venueLocation.Latitude,
            Longitude = venueLocation.Longitude,
            EtaSeconds = elapsedSeconds,
            DistanceFromPreviousMeters = venueLeg.DistanceMeters,
            CumulativeDistanceMeters = elapsedDistanceMeters,
            CumulativeTimeSeconds = elapsedSeconds,
            StopAccessType = "destination"
        });

        var passengerRoutes = input.Passengers
            .Select(passenger =>
            {
                var pickup = pickupSnapshots[passenger.Id];
                var rideTimeSeconds = Math.Max(0, elapsedSeconds - pickup.PickupEtaSeconds);
                var rideDistanceMeters = Math.Max(0, elapsedDistanceMeters - pickup.CumulativeDistanceMeters);
                var walkSeconds = pickup.WalkingDistanceMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
                var burdenScore =
                    rideTimeSeconds +
                    walkSeconds * RoutingDefaults.WalkWeight +
                    pickup.WaitSeconds * RoutingDefaults.WaitWeight +
                    pickup.RiskPenaltySeconds;

                return new MemberRouteDto
                {
                    MemberId = passenger.Id,
                    MemberName = passenger.Name,
                    EstimatedTimeSeconds = rideTimeSeconds + walkSeconds + pickup.WaitSeconds,
                    DistanceMeters = rideDistanceMeters + pickup.WalkingDistanceMeters,
                    RideDistanceMeters = rideDistanceMeters,
                    RideTimeSeconds = rideTimeSeconds,
                    WaitTimeSeconds = pickup.WaitSeconds,
                    DriverId = input.Driver.Id,
                    WalkingDistanceMeters = pickup.WalkingDistanceMeters,
                    BurdenScore = burdenScore
                };
            })
            .ToList();

        var detourPenaltySeconds = Math.Max(0, elapsedSeconds - directRoute.DurationSeconds) * RoutingDefaults.DetourWeight;
        var fairnessPenaltySeconds = ComputeFairnessPenalty(passengerRoutes);
        var stopComplexityPenaltySeconds = orderedStops.Count * RoutingDefaults.StopComplexityWeight;
        var generalizedCost =
            elapsedSeconds +
            walkSecondsTotal * RoutingDefaults.WalkWeight +
            waitSecondsTotal * RoutingDefaults.WaitWeight +
            detourPenaltySeconds +
            fairnessPenaltySeconds +
            stopComplexityPenaltySeconds +
            riskPenaltySeconds;

        return new DriverOptimizationResult
        {
            DriverRoute = new DriverRouteDto
            {
                DriverId = input.Driver.Id,
                DriverName = input.Driver.Name,
                TotalTimeSeconds = elapsedSeconds,
                TotalDistanceMeters = elapsedDistanceMeters,
                DirectTimeSeconds = directRoute.DurationSeconds,
                DirectDistanceMeters = directRoute.DistanceMeters,
                GeneralizedCostSeconds = generalizedCost,
                PassengerIds = input.Passengers.Select(passenger => passenger.Id).ToList(),
                Stops = routeStops
            },
            PassengerRoutes = passengerRoutes,
            CostBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = generalizedCost,
                TotalDriveSeconds = elapsedSeconds,
                TotalWalkSeconds = walkSecondsTotal,
                TotalWaitSeconds = waitSecondsTotal,
                DetourPenaltySeconds = detourPenaltySeconds,
                FairnessPenaltySeconds = fairnessPenaltySeconds,
                StopComplexityPenaltySeconds = stopComplexityPenaltySeconds,
                RiskPenaltySeconds = riskPenaltySeconds,
                StabilityPenaltySeconds = 0,
                VenueQualityBonusSeconds = 0
            }
        };
    }

    private async Task<DriverOptimizationResult> BuildDirectDriverResultAsync(
        DriverOptimizationInput input,
        CancellationToken ct)
    {
        var directRoute = await GetRouteAsync(
            input.Driver.GetLocation(),
            input.Venue.GetLocation(),
            input.Driver.TransportMode,
            input,
            ct);

        var routeStops = new List<RouteStopDto>
        {
            new()
            {
                Sequence = 0,
                StopType = "driver_origin",
                Label = input.Driver.Name,
                Latitude = input.Driver.Latitude,
                Longitude = input.Driver.Longitude,
                EtaSeconds = 0,
                DistanceFromPreviousMeters = 0,
                CumulativeDistanceMeters = 0,
                CumulativeTimeSeconds = 0,
                StopAccessType = "origin"
            },
            new()
            {
                Sequence = 1,
                StopType = "destination",
                Label = input.Venue.Name,
                Latitude = input.Venue.Latitude,
                Longitude = input.Venue.Longitude,
                EtaSeconds = directRoute.DurationSeconds,
                DistanceFromPreviousMeters = directRoute.DistanceMeters,
                CumulativeDistanceMeters = directRoute.DistanceMeters,
                CumulativeTimeSeconds = directRoute.DurationSeconds,
                StopAccessType = "destination"
            }
        };

        return new DriverOptimizationResult
        {
            DriverRoute = new DriverRouteDto
            {
                DriverId = input.Driver.Id,
                DriverName = input.Driver.Name,
                TotalTimeSeconds = directRoute.DurationSeconds,
                TotalDistanceMeters = directRoute.DistanceMeters,
                DirectTimeSeconds = directRoute.DurationSeconds,
                DirectDistanceMeters = directRoute.DistanceMeters,
                GeneralizedCostSeconds = directRoute.DurationSeconds,
                PassengerIds = new List<Guid>(),
                Stops = routeStops
            },
            PassengerRoutes = Array.Empty<MemberRouteDto>(),
            CostBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = directRoute.DurationSeconds,
                TotalDriveSeconds = directRoute.DurationSeconds
            }
        };
    }

    private async Task<RouteResult> GetRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        DriverOptimizationInput input,
        CancellationToken ct) =>
        await _routeCostProvider.GetExactRouteAsync(
            origin,
            destination,
            mode,
            new RouteCostContext(input.PreferTrafficAwareRoutes, input.TrafficSnapshot.BucketKey),
            ct);

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

    private sealed record PickupSnapshot(
        double PickupEtaSeconds,
        double CumulativeDistanceMeters,
        double WalkingDistanceMeters,
        double WaitSeconds,
        double RiskPenaltySeconds);
}
