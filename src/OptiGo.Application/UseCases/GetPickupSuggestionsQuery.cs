using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Services;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.UseCases;

public record GetPickupSuggestionsQuery(Guid SessionId) : IRequest<IReadOnlyList<PickupSuggestionDto>>;

public class PickupSuggestionDto
{
    public Guid PassengerId { get; init; }
    public string PassengerName { get; init; } = string.Empty;
    public Guid DriverId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public double EstimatedDetourSeconds { get; init; }
    public double DistanceToPassengerMeters { get; init; }
    public int RemainingSeatCount { get; init; }
    public double ScoreSeconds { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public class GetPickupSuggestionsHandler : IRequestHandler<GetPickupSuggestionsQuery, IReadOnlyList<PickupSuggestionDto>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IRouteCostProvider _routeCostProvider;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;

    public GetPickupSuggestionsHandler(
        ISessionRepository sessionRepository,
        IRouteCostProvider routeCostProvider,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
        _sessionRepository = sessionRepository;
        _routeCostProvider = routeCostProvider;
        _trafficSnapshotProvider = trafficSnapshotProvider;
    }

    public async Task<IReadOnlyList<PickupSuggestionDto>> Handle(
        GetPickupSuggestionsQuery request,
        CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
            return [];

        var pendingRequests = session.PickupRequests
            .Where(pickupRequest => pickupRequest.IsPending())
            .ToList();
        if (pendingRequests.Count == 0)
            return [];

        var membersById = session.Members.ToDictionary(member => member.Id);
        var drivers = session.Members
            .Where(member => member.CanOfferPickup())
            .Where(member => session.GetAcceptedPassengerCount(member.Id) < member.GetSeatCapacity())
            .ToList();
        if (drivers.Count == 0)
            return [];

        var target = ResolveSuggestionTarget(session);
        var trafficSnapshot = _trafficSnapshotProvider.GetCurrentSnapshot();
        var context = new RouteCostContext(false, trafficSnapshot.BucketKey);
        var suggestions = new List<PickupSuggestionDto>();

        foreach (var pendingRequest in pendingRequests)
        {
            if (!membersById.TryGetValue(pendingRequest.PassengerId, out var passenger))
                continue;

            var passengerLocation = passenger.GetLocation();
            var passengerSuggestions = new List<PickupSuggestionDto>();

            foreach (var driver in drivers.Where(driver => driver.Id != passenger.Id))
            {
                var remainingSeats = driver.GetSeatCapacity() - session.GetAcceptedPassengerCount(driver.Id);
                if (remainingSeats <= 0)
                    continue;

                var driverLocation = driver.GetLocation();
                var directRoute = await _routeCostProvider.GetExactRouteAsync(
                    driverLocation,
                    target,
                    driver.TransportMode,
                    context,
                    cancellationToken);
                var toPassengerRoute = await _routeCostProvider.GetExactRouteAsync(
                    driverLocation,
                    passengerLocation,
                    driver.TransportMode,
                    context,
                    cancellationToken);
                var passengerToTargetRoute = await _routeCostProvider.GetExactRouteAsync(
                    passengerLocation,
                    target,
                    driver.TransportMode,
                    context,
                    cancellationToken);

                var detourSeconds = Math.Max(
                    0,
                    toPassengerRoute.DurationSeconds + passengerToTargetRoute.DurationSeconds - directRoute.DurationSeconds);
                var loadPressureSeconds = session.GetAcceptedPassengerCount(driver.Id) * 45;
                var overDetourPenaltySeconds = Math.Max(
                    0,
                    detourSeconds - 15 * 60) * 2;
                var scoreSeconds = detourSeconds + loadPressureSeconds + overDetourPenaltySeconds;

                passengerSuggestions.Add(new PickupSuggestionDto
                {
                    PassengerId = passenger.Id,
                    PassengerName = passenger.Name,
                    DriverId = driver.Id,
                    DriverName = driver.Name,
                    EstimatedDetourSeconds = detourSeconds,
                    DistanceToPassengerMeters = driverLocation.DistanceTo(passengerLocation),
                    RemainingSeatCount = remainingSeats,
                    ScoreSeconds = scoreSeconds,
                    Reason = BuildReason(driver, detourSeconds, remainingSeats)
                });
            }

            suggestions.AddRange(passengerSuggestions
                .OrderBy(suggestion => suggestion.ScoreSeconds)
                .Take(3));
        }

        return suggestions
            .OrderBy(suggestion => suggestion.PassengerName)
            .ThenBy(suggestion => suggestion.ScoreSeconds)
            .ToList();
    }

    private static Coordinate ResolveSuggestionTarget(Session session)
    {
        var finalVenue = TryResolveVenueFromSnapshot(session.FinalRouteSnapshotJson);
        if (finalVenue.HasValue)
            return finalVenue.Value;

        var latestTopVenue = TryResolveFirstTopVenueFromSnapshot(session.LatestOptimizationSnapshotJson);
        if (latestTopVenue.HasValue)
            return latestTopVenue.Value;

        return OutingSearchCenterCalculator.Calculate(session);
    }

    private static Coordinate? TryResolveVenueFromSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<CandidateResultDto>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            return snapshot == null ? null : new Coordinate(snapshot.Latitude, snapshot.Longitude);
        }
        catch
        {
            return null;
        }
    }

    private static Coordinate? TryResolveFirstTopVenueFromSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<FindMeetPointResult>(
                json,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            var venue = snapshot?.TopVenues?.FirstOrDefault();
            return venue == null ? null : new Coordinate(venue.Latitude, venue.Longitude);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildReason(Member driver, double detourSeconds, int remainingSeats) =>
        $"{driver.Name} còn {remainingSeats} chỗ, dự kiến vòng thêm khoảng {Math.Round(detourSeconds / 60)} phút.";
}
