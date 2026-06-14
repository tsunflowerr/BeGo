using System.Collections.Concurrent;
using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class StopCandidateGenerator : IStopCandidateGenerator
{
    private const double MinGeneratedWalkMeters = 25;
    private const double EarthRadiusMeters = 6_371_000;

    private readonly IMeetingPointProvider _meetingPointProvider;
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<MeetingPointCandidate>>> _pickupPointCache = new();

    public StopCandidateGenerator()
        : this(new NullMeetingPointProvider())
    {
    }

    public StopCandidateGenerator(IMeetingPointProvider meetingPointProvider)
    {
        _meetingPointProvider = meetingPointProvider;
    }

    public Task<IReadOnlyList<StopCandidate>> GenerateAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default) =>
        GenerateCoreAsync(input, ct);

    private async Task<IReadOnlyList<StopCandidate>> GenerateCoreAsync(
        DriverOptimizationInput input,
        CancellationToken ct)
    {
        var candidates = new List<StopCandidate>();
        var driverLocation = input.Driver.GetLocation();
        var venueLocation = input.Venue.GetLocation();

        foreach (var passenger in input.Passengers)
        {
            var passengerLocation = passenger.GetLocation();
            candidates.Add(CreateDoorstepCandidate(passenger, passengerLocation));

            AddIfNotNull(candidates, CreateCorridorCandidate(
                passenger,
                passengerLocation,
                driverLocation,
                venueLocation));

            AddIfNotNull(candidates, CreateDirectionalCandidate(
                passenger,
                passengerLocation,
                driverLocation,
                0.35,
                "driver-approach",
                $"{passenger.Name} (ra điểm đón gần tài xế)",
                "approximate_roadside",
                RoutingDefaults.RoadsideAccessPenaltySeconds,
                RoutingDefaults.ApproximateRoadsideRiskSeconds));

            AddIfNotNull(candidates, CreateDirectionalCandidate(
                passenger,
                passengerLocation,
                venueLocation,
                0.22,
                "venue-approach",
                $"{passenger.Name} (đi bộ ra trục đường thuận tuyến)",
                "venue_approach",
                RoutingDefaults.RoadsideAccessPenaltySeconds * 0.75,
                RoutingDefaults.ApproximateRoadsideRiskSeconds * 0.8));

            var poiStops = await CreatePoiCandidatesAsync(passenger, passengerLocation, ct);
            candidates.AddRange(poiStops);
        }

        candidates.AddRange(CreateSharedCandidates(input.Passengers, driverLocation, venueLocation));

        var deduped = DedupeByLocation(candidates, input.Passengers);
        return LimitPerPassenger(deduped, input.Passengers);
    }

    private static void AddIfNotNull(List<StopCandidate> candidates, StopCandidate? candidate)
    {
        if (candidate != null)
        {
            candidates.Add(candidate);
        }
    }

    private static StopCandidate? CreateCorridorCandidate(
        Member passenger,
        Coordinate passengerLocation,
        Coordinate driverLocation,
        Coordinate venueLocation)
    {
        var projected = ProjectOntoSegment(passengerLocation, driverLocation, venueLocation);
        if (projected == null)
            return null;

        var projectedLocation = projected.Value;
        var walkingMeters = passengerLocation.DistanceTo(projectedLocation);
        if (!IsGeneratedWalkFeasible(walkingMeters))
            return null;

        return new StopCandidate
        {
            CandidateId = $"{passenger.Id}:driver-corridor",
            StopLocation = projectedLocation,
            Label = $"{passenger.Name} (đón trên tuyến tài xế)",
            StopAccessType = "driver_corridor",
            PassengerIds = [passenger.Id],
            WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = walkingMeters },
            AccessPenaltySeconds = RoutingDefaults.RoadsideAccessPenaltySeconds * 0.6,
            RiskPenaltySeconds = RoutingDefaults.ApproximateRoadsideRiskSeconds * 0.55
        };
    }

    private static StopCandidate? CreateDirectionalCandidate(
        Member passenger,
        Coordinate passengerLocation,
        Coordinate target,
        double ratio,
        string suffix,
        string label,
        string stopAccessType,
        double accessPenaltySeconds,
        double riskPenaltySeconds)
    {
        var walkingMeters = ComputeWalkDistance(passengerLocation.DistanceTo(target), ratio);
        if (!IsGeneratedWalkFeasible(walkingMeters))
            return null;

        var stopLocation = MoveToward(passengerLocation, target, walkingMeters);
        return new StopCandidate
        {
            CandidateId = $"{passenger.Id}:{suffix}",
            StopLocation = stopLocation,
            Label = label,
            StopAccessType = stopAccessType,
            PassengerIds = [passenger.Id],
            WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = walkingMeters },
            AccessPenaltySeconds = accessPenaltySeconds,
            RiskPenaltySeconds = riskPenaltySeconds
        };
    }

    private static IEnumerable<StopCandidate> CreateSharedCandidates(
        IReadOnlyList<Member> passengers,
        Coordinate driverLocation,
        Coordinate venueLocation)
    {
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < passengers.Count; i++)
        {
            for (var j = i + 1; j < passengers.Count; j++)
            {
                var first = passengers[i];
                var second = passengers[j];
                if (first.GetLocation().DistanceTo(second.GetLocation()) > RoutingDefaults.SharedClusterRadiusMeters)
                    continue;

                var group = new[] { first, second };
                var candidate = CreateSharedCandidate(group, driverLocation, venueLocation, seenGroups);
                if (candidate != null)
                    yield return candidate;
            }
        }

        foreach (var seed in passengers)
        {
            var seedLocation = seed.GetLocation();
            var cluster = passengers
                .Where(passenger => passenger.GetLocation().DistanceTo(seedLocation) <= RoutingDefaults.SharedClusterRadiusMeters)
                .OrderBy(passenger => passenger.GetLocation().DistanceTo(seedLocation))
                .Take(RoutingDefaults.MaxSharedStopsPerCluster)
                .ToList();

            if (cluster.Count < 3)
                continue;

            var candidate = CreateSharedCandidate(cluster, driverLocation, venueLocation, seenGroups);
            if (candidate != null)
                yield return candidate;
        }
    }

    private async Task<IReadOnlyList<StopCandidate>> CreatePoiCandidatesAsync(
        Member passenger,
        Coordinate passengerLocation,
        CancellationToken ct)
    {
        var pickupPoints = await GetCachedPickupPointsAsync(
            passenger,
            passengerLocation,
            RoutingDefaults.MaxWalkDistanceMeters,
            RoutingDefaults.MaxStopsPerPassenger,
            ct);
        var result = new List<StopCandidate>();

        foreach (var pickupPoint in pickupPoints)
        {
            var walkingMeters = passengerLocation.DistanceTo(pickupPoint.Location);
            if (!IsWalkFeasible(walkingMeters))
                continue;

            result.Add(new StopCandidate
            {
                CandidateId = $"{passenger.Id}:poi:{pickupPoint.Id}",
                StopLocation = pickupPoint.Location,
                Label = BuildPoiLabel(pickupPoint),
                StopAccessType = "poi_landmark",
                PassengerIds = [passenger.Id],
                WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = walkingMeters },
                AccessPenaltySeconds = Math.Max(0, 18 - pickupPoint.PickupFriendlyScore * 12),
                RiskPenaltySeconds = Math.Max(2, 18 - pickupPoint.PickupFriendlyScore * 14)
            });
        }

        return result;
    }

    private async Task<IReadOnlyList<MeetingPointCandidate>> GetCachedPickupPointsAsync(
        Member passenger,
        Coordinate passengerLocation,
        double radiusMeters,
        int limit,
        CancellationToken ct)
    {
        var key = BuildPickupPointCacheKey(passenger, passengerLocation, radiusMeters, limit);
        var task = _pickupPointCache.GetOrAdd(key, _ => _meetingPointProvider.SearchPickupPointsAsync(
            passengerLocation,
            radiusMeters,
            limit,
            ct));

        try
        {
            return await task;
        }
        catch
        {
            _pickupPointCache.TryRemove(key, out _);
            throw;
        }
    }

    private static string BuildPickupPointCacheKey(
        Member passenger,
        Coordinate passengerLocation,
        double radiusMeters,
        int limit) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{passenger.Id:N}|{passengerLocation.Latitude:F5}|{passengerLocation.Longitude:F5}|{radiusMeters:F0}|{limit}");

    private static StopCandidate? CreateSharedCandidate(
        IReadOnlyList<Member> group,
        Coordinate driverLocation,
        Coordinate venueLocation,
        HashSet<string> seenGroups)
    {
        var orderedGroup = group
            .OrderBy(passenger => passenger.Id)
            .ToList();
        var groupKey = string.Join(":", orderedGroup.Select(passenger => passenger.Id.ToString("N")));
        if (!seenGroups.Add(groupKey))
            return null;

        var stopLocation = ChooseSharedStopLocation(orderedGroup, driverLocation, venueLocation);
        var walkingDistances = BuildWalkingDistances(orderedGroup, stopLocation);
        if (walkingDistances == null)
            return null;

        return new StopCandidate
        {
            CandidateId = $"{groupKey}:shared",
            StopLocation = stopLocation,
            Label = BuildSharedLabel(orderedGroup),
            StopAccessType = orderedGroup.Count > 2 ? "shared_cluster_meetpoint" : "shared_meetpoint",
            PassengerIds = orderedGroup.Select(passenger => passenger.Id).ToList(),
            WalkingDistancesMeters = walkingDistances,
            AccessPenaltySeconds = RoutingDefaults.SharedStopAccessPenaltySeconds + Math.Max(0, orderedGroup.Count - 2) * 4,
            RiskPenaltySeconds = RoutingDefaults.SharedStopRiskSeconds + Math.Max(0, orderedGroup.Count - 2) * 3
        };
    }

    private static Coordinate ChooseSharedStopLocation(
        IReadOnlyList<Member> group,
        Coordinate driverLocation,
        Coordinate venueLocation)
    {
        var centroid = new Coordinate(
            group.Average(passenger => passenger.Latitude),
            group.Average(passenger => passenger.Longitude));

        var corridorProjection = ProjectOntoSegment(centroid, driverLocation, venueLocation);
        if (corridorProjection == null)
            return centroid;

        var corridorLocation = corridorProjection.Value;
        var centroidAverageWalk = group.Average(passenger => passenger.GetLocation().DistanceTo(centroid));
        var corridorAverageWalk = group.Average(passenger => passenger.GetLocation().DistanceTo(corridorLocation));
        return IsSharedStopFeasible(group, corridorLocation) &&
               corridorAverageWalk <= centroidAverageWalk + 50
            ? corridorLocation
            : centroid;
    }

    private static Dictionary<Guid, double>? BuildWalkingDistances(
        IReadOnlyList<Member> passengers,
        Coordinate stopLocation)
    {
        var result = new Dictionary<Guid, double>();
        foreach (var passenger in passengers)
        {
            var walkingMeters = passenger.GetLocation().DistanceTo(stopLocation);
            if (!IsWalkFeasible(walkingMeters) ||
                walkingMeters / RoutingDefaults.WalkSpeedMetersPerSecond > RoutingDefaults.SharedStopTargetWalkSeconds)
            {
                return null;
            }

            result[passenger.Id] = walkingMeters;
        }

        return result;
    }

    private static bool IsSharedStopFeasible(IReadOnlyList<Member> group, Coordinate stopLocation) =>
        group.All(passenger => IsWalkFeasible(passenger.GetLocation().DistanceTo(stopLocation)));

    private static List<StopCandidate> DedupeByLocation(
        IReadOnlyList<StopCandidate> candidates,
        IReadOnlyList<Member> passengers)
    {
        var namesById = passengers.ToDictionary(passenger => passenger.Id, passenger => passenger.Name);
        var result = new List<StopCandidate>();

        foreach (var candidate in candidates.OrderBy(CandidateSortKey))
        {
            var matchIndex = result.FindIndex(existing =>
                existing.StopLocation.DistanceTo(candidate.StopLocation) < RoutingDefaults.StopDeduplicationMeters);

            if (matchIndex < 0)
            {
                result.Add(candidate);
                continue;
            }

            result[matchIndex] = MergeCandidates(result[matchIndex], candidate, namesById);
        }

        return result;
    }

    private static StopCandidate MergeCandidates(
        StopCandidate existing,
        StopCandidate incoming,
        IReadOnlyDictionary<Guid, string> namesById)
    {
        var offsetMeters = existing.StopLocation.DistanceTo(incoming.StopLocation);
        var walkingDistances = new Dictionary<Guid, double>(existing.WalkingDistancesMeters);

        foreach (var (passengerId, incomingWalkMeters) in incoming.WalkingDistancesMeters)
        {
            var adjustedWalkMeters = incomingWalkMeters + offsetMeters;
            if (walkingDistances.TryGetValue(passengerId, out var existingWalkMeters))
            {
                walkingDistances[passengerId] = Math.Min(existingWalkMeters, adjustedWalkMeters);
            }
            else if (IsWalkFeasible(adjustedWalkMeters))
            {
                walkingDistances[passengerId] = adjustedWalkMeters;
            }
        }

        var passengerIds = walkingDistances.Keys
            .OrderBy(id => id)
            .ToList();

        return new StopCandidate
        {
            CandidateId = string.Join(":", passengerIds.Select(id => id.ToString("N"))) + ":deduped",
            StopLocation = existing.StopLocation,
            Label = passengerIds.Count > 1 ? BuildMergedLabel(passengerIds, namesById) : existing.Label,
            StopAccessType = ResolveMergedAccessType(existing, incoming, passengerIds.Count),
            PassengerIds = passengerIds,
            WalkingDistancesMeters = walkingDistances,
            AccessPenaltySeconds = passengerIds.Count > 1
                ? Math.Min(existing.AccessPenaltySeconds, incoming.AccessPenaltySeconds + offsetMeters / RoutingDefaults.WalkSpeedMetersPerSecond)
                : existing.AccessPenaltySeconds,
            RiskPenaltySeconds = Math.Min(existing.RiskPenaltySeconds, incoming.RiskPenaltySeconds)
        };
    }

    private static string ResolveMergedAccessType(
        StopCandidate existing,
        StopCandidate incoming,
        int passengerCount)
    {
        if (passengerCount <= 1)
            return existing.StopAccessType;

        return passengerCount > 2 ||
               existing.StopAccessType == "shared_cluster_meetpoint" ||
               incoming.StopAccessType == "shared_cluster_meetpoint"
            ? "shared_cluster_meetpoint"
            : "shared_meetpoint";
    }

    private static List<StopCandidate> LimitPerPassenger(
        IReadOnlyList<StopCandidate> candidates,
        IReadOnlyList<Member> passengers)
    {
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var passenger in passengers)
        {
            var passengerOptions = candidates
                .Where(candidate => candidate.PassengerIds.Contains(passenger.Id))
                .OrderBy(candidate => CandidatePassengerScore(candidate, passenger.Id))
                .Take(RoutingDefaults.MaxStopsPerPassenger)
                .ToList();

            foreach (var candidate in passengerOptions)
            {
                selectedIds.Add(candidate.CandidateId);
            }
        }

        return candidates
            .Where(candidate => selectedIds.Contains(candidate.CandidateId))
            .OrderBy(CandidateSortKey)
            .ToList();
    }

    private static double CandidateSortKey(StopCandidate candidate) =>
        (candidate.StopAccessType == "doorstep" ? 0 : 1_000) -
        candidate.PassengerIds.Count * 100 +
        candidate.AccessPenaltySeconds +
        candidate.RiskPenaltySeconds;

    private static double CandidatePassengerScore(StopCandidate candidate, Guid passengerId)
    {
        var walkingMeters = candidate.WalkingDistancesMeters.TryGetValue(passengerId, out var value) ? value : 0;
        var walkingSeconds = walkingMeters / RoutingDefaults.WalkSpeedMetersPerSecond;
        var coverageBonusSeconds = Math.Max(0, candidate.PassengerIds.Count - 1) * 35;
        return walkingSeconds + candidate.AccessPenaltySeconds + candidate.RiskPenaltySeconds - coverageBonusSeconds;
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

    private static string BuildSharedLabel(IReadOnlyList<Member> group)
    {
        var names = group.Take(2).Select(passenger => passenger.Name).ToList();
        var label = string.Join(" + ", names);
        if (group.Count > 2)
        {
            label += $" + {group.Count - 2} người";
        }

        return $"{label} (điểm đón chung)";
    }

    private static string BuildMergedLabel(
        IReadOnlyList<Guid> passengerIds,
        IReadOnlyDictionary<Guid, string> namesById)
    {
        var names = passengerIds
            .Take(2)
            .Select(id => namesById.TryGetValue(id, out var name) ? name : "Khách")
            .ToList();
        var label = string.Join(" + ", names);
        if (passengerIds.Count > 2)
        {
            label += $" + {passengerIds.Count - 2} người";
        }

        return $"{label} (điểm đón chung)";
    }

    private static string BuildPoiLabel(MeetingPointCandidate pickupPoint) =>
        string.IsNullOrWhiteSpace(pickupPoint.Address)
            ? $"Đón tại {pickupPoint.Name}"
            : $"Đón tại {pickupPoint.Name}";

    private static double ComputeWalkDistance(double rawDistanceMeters, double ratio) =>
        Math.Min(RoutingDefaults.MaxWalkDistanceMeters, rawDistanceMeters * ratio);

    private static bool IsGeneratedWalkFeasible(double walkingMeters) =>
        walkingMeters >= MinGeneratedWalkMeters && IsWalkFeasible(walkingMeters);

    private static bool IsWalkFeasible(double walkingMeters) =>
        walkingMeters <= RoutingDefaults.MaxWalkDistanceMeters &&
        walkingMeters / RoutingDefaults.WalkSpeedMetersPerSecond <= RoutingDefaults.MaxWalkSeconds;

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

    private static Coordinate? ProjectOntoSegment(Coordinate point, Coordinate segmentStart, Coordinate segmentEnd)
    {
        var start = ToLocalMeters(segmentStart, point);
        var end = ToLocalMeters(segmentEnd, point);
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;

        if (lengthSquared < 1)
            return null;

        var t = Math.Clamp(-(start.X * segmentX + start.Y * segmentY) / lengthSquared, 0, 1);
        var projectedX = start.X + segmentX * t;
        var projectedY = start.Y + segmentY * t;
        return FromLocalMeters(projectedX, projectedY, point);
    }

    private static (double X, double Y) ToLocalMeters(Coordinate coordinate, Coordinate origin)
    {
        var latitudeRadians = DegreesToRadians(origin.Latitude);
        var x = DegreesToRadians(coordinate.Longitude - origin.Longitude) * EarthRadiusMeters * Math.Cos(latitudeRadians);
        var y = DegreesToRadians(coordinate.Latitude - origin.Latitude) * EarthRadiusMeters;
        return (x, y);
    }

    private static Coordinate FromLocalMeters(double x, double y, Coordinate origin)
    {
        var latitude = origin.Latitude + RadiansToDegrees(y / EarthRadiusMeters);
        var longitudeScale = EarthRadiusMeters * Math.Cos(DegreesToRadians(origin.Latitude));
        var longitude = Math.Abs(longitudeScale) < 1e-9
            ? origin.Longitude
            : origin.Longitude + RadiansToDegrees(x / longitudeScale);
        return new Coordinate(latitude, longitude);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private sealed class NullMeetingPointProvider : IMeetingPointProvider
    {
        public Task<IReadOnlyList<MeetingPointCandidate>> SearchPickupPointsAsync(
            Coordinate passengerLocation,
            double radiusMeters,
            int limit = 16,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MeetingPointCandidate>>([]);
    }
}
