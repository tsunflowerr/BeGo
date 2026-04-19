using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;

namespace OptiGo.Infrastructure.Routing;

public class RouteAwareVenuePrefilter : IVenuePrefilter
{
    private readonly IRouteCostProvider _routeCostProvider;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;

    public RouteAwareVenuePrefilter(
        IRouteCostProvider routeCostProvider,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
        _routeCostProvider = routeCostProvider;
        _trafficSnapshotProvider = trafficSnapshotProvider;
    }

    public async Task<IReadOnlyList<Venue>> FilterTopCandidatesAsync(
        Session session,
        IReadOnlyList<Venue> rawVenues,
        int topN = 15,
        CancellationToken ct = default)
    {
        if (rawVenues.Count <= topN)
            return rawVenues;

        var trafficSnapshot = _trafficSnapshotProvider.GetCurrentSnapshot();
        var context = new RouteCostContext(false, trafficSnapshot.BucketKey);
        var acceptedByDriver = session.PickupRequests
            .Where(request => request.IsAccepted() && request.AcceptedDriverId.HasValue)
            .GroupBy(request => request.AcceptedDriverId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var membersById = session.Members.ToDictionary(member => member.Id);
        var venueLocations = rawVenues.Select(venue => venue.GetLocation()).ToList();
        var scores = rawVenues.ToDictionary(venue => venue.Id, _ => 0d);

        await AddDriverClusterCostsAsync(
            session.Members.Where(member => member.CanOfferPickup()).ToList(),
            acceptedByDriver,
            membersById,
            venueLocations,
            scores,
            rawVenues,
            context,
            ct);

        await AddSelfTravelCostsAsync(
            session.Members
                .Where(member => !member.CanOfferPickup() && !member.NeedsPickup())
                .ToList(),
            venueLocations,
            scores,
            rawVenues,
            context,
            ct);

        if (session.PickupRequests.Any(request => !request.IsAccepted()))
        {
            foreach (var venue in rawVenues)
            {
                scores[venue.Id] += 900;
            }
        }

        return rawVenues
            .OrderBy(venue => scores[venue.Id])
            .Take(topN)
            .ToList();
    }

    private async Task AddDriverClusterCostsAsync(
        IReadOnlyList<Member> drivers,
        IReadOnlyDictionary<Guid, List<PickupRequest>> acceptedByDriver,
        IReadOnlyDictionary<Guid, Member> membersById,
        IReadOnlyList<OptiGo.Domain.ValueObjects.Coordinate> venueLocations,
        Dictionary<string, double> scores,
        IReadOnlyList<Venue> venues,
        RouteCostContext context,
        CancellationToken ct)
    {
        foreach (var modeGroup in drivers.GroupBy(driver => driver.TransportMode))
        {
            var groupedDrivers = modeGroup.ToList();
            var matrix = await _routeCostProvider.GetEstimatedMatrixAsync(
                groupedDrivers.Select(driver => driver.GetLocation()).ToList(),
                venueLocations,
                modeGroup.Key,
                context,
                ct);

            for (var driverIndex = 0; driverIndex < groupedDrivers.Count; driverIndex++)
            {
                var driver = groupedDrivers[driverIndex];
                acceptedByDriver.TryGetValue(driver.Id, out var requests);
                var passengers = requests?.Select(request => membersById[request.PassengerId]).ToList() ?? [];
                var clusterAccessSeconds = EstimateClusterAccessSeconds(driver, passengers);
                var passengerCount = passengers.Count;
                var clusterMultiplier = 1.0 + passengerCount * 0.22;

                for (var venueIndex = 0; venueIndex < venues.Count; venueIndex++)
                {
                    var driveSeconds = matrix.Durations[driverIndex, venueIndex];
                    var clusterScore =
                        driveSeconds * clusterMultiplier +
                        clusterAccessSeconds +
                        passengerCount * 35;

                    scores[venues[venueIndex].Id] += clusterScore;
                }
            }
        }
    }

    private async Task AddSelfTravelCostsAsync(
        IReadOnlyList<Member> members,
        IReadOnlyList<OptiGo.Domain.ValueObjects.Coordinate> venueLocations,
        Dictionary<string, double> scores,
        IReadOnlyList<Venue> venues,
        RouteCostContext context,
        CancellationToken ct)
    {
        foreach (var modeGroup in members.GroupBy(member => member.TransportMode))
        {
            var groupedMembers = modeGroup.ToList();
            if (groupedMembers.Count == 0)
                continue;

            var matrix = await _routeCostProvider.GetEstimatedMatrixAsync(
                groupedMembers.Select(member => member.GetLocation()).ToList(),
                venueLocations,
                modeGroup.Key,
                context,
                ct);

            for (var originIndex = 0; originIndex < groupedMembers.Count; originIndex++)
            {
                for (var venueIndex = 0; venueIndex < venues.Count; venueIndex++)
                {
                    scores[venues[venueIndex].Id] += matrix.Durations[originIndex, venueIndex];
                }
            }
        }
    }

    private static double EstimateClusterAccessSeconds(Member driver, IReadOnlyList<Member> passengers)
    {
        if (passengers.Count == 0)
            return 0;

        var averageDistanceToDriver = passengers
            .Average(passenger => passenger.GetLocation().DistanceTo(driver.GetLocation()));

        var boundedWalkDistance = Math.Min(RoutingDefaults.MaxWalkDistanceMeters, averageDistanceToDriver * 0.35);
        return boundedWalkDistance / RoutingDefaults.WalkSpeedMetersPerSecond;
    }
}
