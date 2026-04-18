using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class HeuristicOutingRoutePlanner : IOutingRoutePlanner
{
    private const double MaxWalkDistanceMeters = 400;
    private const double WalkSpeedMetersPerSecond = 1.25;

    private readonly ITravelTimeService _travelTimeService;

    public HeuristicOutingRoutePlanner(ITravelTimeService travelTimeService)
    {
        _travelTimeService = travelTimeService;
    }

    public async Task<CandidateResultDto> PlanVenueAsync(
        Session session,
        Venue venue,
        CancellationToken ct = default)
    {
        var membersById = session.Members.ToDictionary(member => member.Id);
        var acceptedRequests = session.PickupRequests
            .Where(request => request.IsAccepted())
            .ToList();
        var requestsByDriver = acceptedRequests
            .GroupBy(request => request.AcceptedDriverId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var driverRoutes = new List<DriverRouteDto>();
        var memberRoutes = new List<MemberRouteDto>();
        var assignedPassengerIds = acceptedRequests.Select(request => request.PassengerId).ToHashSet();
        var routeCache = new Dictionary<string, RouteResult>();

        foreach (var driver in session.Members.Where(member => member.CanOfferPickup()))
        {
            requestsByDriver.TryGetValue(driver.Id, out var driverRequests);
            var route = await BuildDriverRouteAsync(driver, driverRequests ?? [], membersById, venue, routeCache, ct);
            driverRoutes.Add(route.Route);

            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = driver.Id,
                MemberName = driver.Name,
                EstimatedTimeSeconds = route.Route.TotalTimeSeconds,
                DistanceMeters = route.Route.TotalDistanceMeters,
                DriverId = driver.Id,
                WalkingDistanceMeters = 0
            });

            memberRoutes.AddRange(route.PassengerRoutes);
        }

        foreach (var member in session.Members.Where(member =>
                     !member.CanOfferPickup() &&
                     !assignedPassengerIds.Contains(member.Id)))
        {
            var directRoute = await GetRouteCachedAsync(
                routeCache,
                member.GetLocation(),
                venue.GetLocation(),
                member.TransportMode,
                ct);

            memberRoutes.Add(new MemberRouteDto
            {
                MemberId = member.Id,
                MemberName = member.Name,
                EstimatedTimeSeconds = directRoute.DurationSeconds,
                DistanceMeters = directRoute.DistanceMeters,
                DriverId = null,
                WalkingDistanceMeters = 0
            });
        }

        var driverDetours = driverRoutes
            .Select(route => Math.Max(0, route.TotalTimeSeconds - route.DirectTimeSeconds))
            .DefaultIfEmpty(0)
            .ToList();

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
            MaxDriverDetourSeconds = driverDetours.Max(),
            TotalWalkingDistanceMeters = memberRoutes.Sum(route => route.WalkingDistanceMeters),
            MemberRoutes = memberRoutes.OrderBy(route => route.MemberName).ToList(),
            DriverRoutes = driverRoutes.OrderBy(route => route.DriverName).ToList()
        };
    }

    private async Task<DriverRouteBuildResult> BuildDriverRouteAsync(
        Member driver,
        IReadOnlyList<PickupRequest> driverRequests,
        IReadOnlyDictionary<Guid, Member> membersById,
        Venue venue,
        Dictionary<string, RouteResult> routeCache,
        CancellationToken ct)
    {
        var venueLocation = venue.GetLocation();
        var directRoute = await GetRouteCachedAsync(routeCache, driver.GetLocation(), venueLocation, driver.TransportMode, ct);
        var candidateStops = new List<PickupStopCandidate>();

        foreach (var request in driverRequests)
        {
            var passenger = membersById[request.PassengerId];
            candidateStops.Add(await SelectPickupStopAsync(driver, passenger, venueLocation, routeCache, ct));
        }

        var orderedStops = await OrderStopsAsync(driver, candidateStops, routeCache, ct);
        var routeStops = new List<RouteStopDto>();
        var passengerRoutes = new List<MemberRouteDto>();

        double elapsedSeconds = 0;
        double elapsedDistanceMeters = 0;
        var current = driver.GetLocation();

        routeStops.Add(new RouteStopDto
        {
            Sequence = 0,
            StopType = "driver_origin",
            Label = driver.Name,
            Latitude = current.Latitude,
            Longitude = current.Longitude,
            EtaSeconds = 0,
            DistanceFromPreviousMeters = 0
        });

        foreach (var stop in orderedStops)
        {
            var leg = await GetRouteCachedAsync(routeCache, current, stop.StopLocation, driver.TransportMode, ct);
            elapsedSeconds += leg.DurationSeconds;
            elapsedDistanceMeters += leg.DistanceMeters;
            current = stop.StopLocation;

            routeStops.Add(new RouteStopDto
            {
                Sequence = routeStops.Count,
                StopType = stop.WalkingDistanceMeters > 0 ? "pickup_meetpoint" : "pickup",
                Label = stop.Label,
                Latitude = stop.StopLocation.Latitude,
                Longitude = stop.StopLocation.Longitude,
                EtaSeconds = elapsedSeconds,
                DistanceFromPreviousMeters = leg.DistanceMeters,
                WalkingDistanceMeters = stop.WalkingDistanceMeters,
                PassengerIds = [stop.PassengerId]
            });
        }

        var venueLeg = await GetRouteCachedAsync(routeCache, current, venueLocation, driver.TransportMode, ct);
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
            DistanceFromPreviousMeters = venueLeg.DistanceMeters
        });

        var driverRoute = new DriverRouteDto
        {
            DriverId = driver.Id,
            DriverName = driver.Name,
            TotalTimeSeconds = elapsedSeconds,
            TotalDistanceMeters = elapsedDistanceMeters,
            DirectTimeSeconds = directRoute.DurationSeconds,
            DirectDistanceMeters = directRoute.DistanceMeters,
            PassengerIds = orderedStops.Select(stop => stop.PassengerId).ToList(),
            Stops = routeStops
        };

        foreach (var stop in orderedStops)
        {
            var stopDto = routeStops.First(routeStop => routeStop.PassengerIds.Contains(stop.PassengerId));
            var inVehicleSeconds = Math.Max(0, elapsedSeconds - stopDto.EtaSeconds);
            var walkingSeconds = stop.WalkingDistanceMeters / WalkSpeedMetersPerSecond;

            passengerRoutes.Add(new MemberRouteDto
            {
                MemberId = stop.PassengerId,
                MemberName = stop.PassengerName,
                EstimatedTimeSeconds = walkingSeconds + inVehicleSeconds,
                DistanceMeters = Math.Max(0, elapsedDistanceMeters - DistanceAtStop(routeStops, stop.PassengerId)) + stop.WalkingDistanceMeters,
                DriverId = driver.Id,
                WalkingDistanceMeters = stop.WalkingDistanceMeters
            });
        }

        return new DriverRouteBuildResult(driverRoute, passengerRoutes);
    }

    private static double DistanceAtStop(IEnumerable<RouteStopDto> stops, Guid passengerId)
    {
        return stops
            .Where(stop => stop.PassengerIds.Contains(passengerId))
            .Select(stop => stop.DistanceFromPreviousMeters)
            .FirstOrDefault();
    }

    private async Task<List<PickupStopCandidate>> OrderStopsAsync(
        Member driver,
        IReadOnlyList<PickupStopCandidate> stops,
        Dictionary<string, RouteResult> routeCache,
        CancellationToken ct)
    {
        var remainingStops = stops.ToList();
        var orderedStops = new List<PickupStopCandidate>();
        var current = driver.GetLocation();

        while (remainingStops.Count > 0)
        {
            PickupStopCandidate? bestStop = null;
            double bestDuration = double.MaxValue;

            foreach (var stop in remainingStops)
            {
                var route = await GetRouteCachedAsync(routeCache, current, stop.StopLocation, driver.TransportMode, ct);
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
        Dictionary<string, RouteResult> routeCache,
        CancellationToken ct)
    {
        var passengerLocation = passenger.GetLocation();
        var doorstepRoute = await GetRouteCachedAsync(routeCache, driver.GetLocation(), passengerLocation, driver.TransportMode, ct);
        var doorstepToVenue = await GetRouteCachedAsync(routeCache, passengerLocation, venueLocation, driver.TransportMode, ct);

        var best = new PickupStopCandidate(
            passenger.Id,
            passenger.Name,
            passengerLocation,
            passengerLocation,
            0,
            $"{passenger.Name} (đón tận nơi)",
            doorstepRoute.DurationSeconds + doorstepToVenue.DurationSeconds + (doorstepRoute.DistanceMeters + doorstepToVenue.DistanceMeters) / 1000);

        var driverDistanceToPassenger = driver.GetLocation().DistanceTo(passengerLocation);
        if (driverDistanceToPassenger < 30)
            return best;

        var walkDistance = Math.Min(MaxWalkDistanceMeters, driverDistanceToPassenger * 0.35);
        if (walkDistance < 25)
            return best;

        var walkingStop = MoveToward(passengerLocation, driver.GetLocation(), walkDistance);
        var walkRoute = await GetRouteCachedAsync(routeCache, driver.GetLocation(), walkingStop, driver.TransportMode, ct);
        var walkToVenue = await GetRouteCachedAsync(routeCache, walkingStop, venueLocation, driver.TransportMode, ct);
        var walkPenaltySeconds = walkDistance / WalkSpeedMetersPerSecond;
        var walkScore = walkRoute.DurationSeconds + walkToVenue.DurationSeconds + walkPenaltySeconds * 1.2;

        if (walkScore < best.SortScore)
        {
            best = new PickupStopCandidate(
                passenger.Id,
                passenger.Name,
                passengerLocation,
                walkingStop,
                walkDistance,
                $"{passenger.Name} (đi bộ tới điểm đón)",
                walkScore);
        }

        return best;
    }

    private async Task<RouteResult> GetRouteCachedAsync(
        Dictionary<string, RouteResult> cache,
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        CancellationToken ct)
    {
        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{origin.Latitude:F6}|{origin.Longitude:F6}|{destination.Latitude:F6}|{destination.Longitude:F6}|{(int)mode}");

        if (cache.TryGetValue(key, out var cached))
            return cached;

        var route = await _travelTimeService.GetRouteAsync(origin, destination, mode, ct);
        cache[key] = route;
        return route;
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
        Coordinate PassengerLocation,
        Coordinate StopLocation,
        double WalkingDistanceMeters,
        string Label,
        double SortScore);

    private sealed record DriverRouteBuildResult(
        DriverRouteDto Route,
        IReadOnlyList<MemberRouteDto> PassengerRoutes);
}
