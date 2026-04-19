using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class StopCandidateGenerator : IStopCandidateGenerator
{
    private const double MaxMergedStopSpreadMeters = 250;

    public Task<IReadOnlyList<StopCandidate>> GenerateAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default)
    {
        var candidates = new List<StopCandidate>();
        var driverLocation = input.Driver.GetLocation();
        var venueLocation = input.Venue.GetLocation();

        foreach (var passenger in input.Passengers)
        {
            var passengerLocation = passenger.GetLocation();
            candidates.Add(CreateDoorstepCandidate(passenger, passengerLocation));

            var towardDriverDistance = ComputeWalkDistance(passengerLocation.DistanceTo(driverLocation), 0.35);
            if (towardDriverDistance >= 25)
            {
                var towardDriver = MoveToward(passengerLocation, driverLocation, towardDriverDistance);
                candidates.Add(new StopCandidate
                {
                    CandidateId = $"{passenger.Id}:driver-approach",
                    StopLocation = towardDriver,
                    Label = $"{passenger.Name} (ra điểm đón gần tài xế)",
                    StopAccessType = "approximate_roadside",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = towardDriverDistance },
                    AccessPenaltySeconds = RoutingDefaults.RoadsideAccessPenaltySeconds,
                    RiskPenaltySeconds = RoutingDefaults.ApproximateRoadsideRiskSeconds
                });
            }

            var towardVenueDistance = ComputeWalkDistance(passengerLocation.DistanceTo(venueLocation), 0.22);
            if (towardVenueDistance >= 25)
            {
                var towardVenue = MoveToward(passengerLocation, venueLocation, towardVenueDistance);
                candidates.Add(new StopCandidate
                {
                    CandidateId = $"{passenger.Id}:venue-approach",
                    StopLocation = towardVenue,
                    Label = $"{passenger.Name} (đi bộ ra trục đường thuận tuyến)",
                    StopAccessType = "venue_approach",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = towardVenueDistance },
                    AccessPenaltySeconds = RoutingDefaults.RoadsideAccessPenaltySeconds * 0.75,
                    RiskPenaltySeconds = RoutingDefaults.ApproximateRoadsideRiskSeconds * 0.8
                });
            }
        }

        candidates.AddRange(CreateMergedCandidates(input.Passengers));
        return Task.FromResult<IReadOnlyList<StopCandidate>>(candidates);
    }

    private static IEnumerable<StopCandidate> CreateMergedCandidates(IReadOnlyList<Member> passengers)
    {
        for (var i = 0; i < passengers.Count; i++)
        {
            for (var j = i + 1; j < passengers.Count; j++)
            {
                var first = passengers[i];
                var second = passengers[j];
                var firstLocation = first.GetLocation();
                var secondLocation = second.GetLocation();
                var spread = firstLocation.DistanceTo(secondLocation);

                if (spread > MaxMergedStopSpreadMeters)
                    continue;

                var midpoint = Midpoint(firstLocation, secondLocation);
                var firstWalk = firstLocation.DistanceTo(midpoint);
                var secondWalk = secondLocation.DistanceTo(midpoint);

                if (firstWalk > RoutingDefaults.MaxWalkDistanceMeters ||
                    secondWalk > RoutingDefaults.MaxWalkDistanceMeters)
                {
                    continue;
                }

                yield return new StopCandidate
                {
                    CandidateId = $"{first.Id}:{second.Id}:merged",
                    StopLocation = midpoint,
                    Label = $"{first.Name} + {second.Name} (điểm đón chung)",
                    StopAccessType = "shared_meetpoint",
                    PassengerIds = [first.Id, second.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double>
                    {
                        [first.Id] = firstWalk,
                        [second.Id] = secondWalk
                    },
                    AccessPenaltySeconds = RoutingDefaults.SharedStopAccessPenaltySeconds,
                    RiskPenaltySeconds = RoutingDefaults.SharedStopRiskSeconds
                };
            }
        }
    }

    private static StopCandidate CreateDoorstepCandidate(Member passenger, Coordinate location) =>
        new()
        {
            CandidateId = $"{passenger.Id}:doorstep",
            StopLocation = location,
            Label = $"{passenger.Name} (đón tận nơi)",
            StopAccessType = "doorstep",
            PassengerIds = [passenger.Id],
            WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = 0 },
            AccessPenaltySeconds = 0,
            RiskPenaltySeconds = 0
        };

    private static double ComputeWalkDistance(double rawDistanceMeters, double ratio) =>
        Math.Min(RoutingDefaults.MaxWalkDistanceMeters, rawDistanceMeters * ratio);

    private static Coordinate Midpoint(Coordinate a, Coordinate b) =>
        new((a.Latitude + b.Latitude) / 2.0, (a.Longitude + b.Longitude) / 2.0);

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
}
