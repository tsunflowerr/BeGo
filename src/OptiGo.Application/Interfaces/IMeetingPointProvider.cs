using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.Interfaces;

public interface IMeetingPointProvider
{
    Task<IReadOnlyList<MeetingPointCandidate>> SearchPickupPointsAsync(
        Coordinate passengerLocation,
        double radiusMeters,
        int limit = 16,
        CancellationToken ct = default);
}

public class MeetingPointCandidate
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public Coordinate Location { get; init; }
    public string? Address { get; init; }
    public double PickupFriendlyScore { get; init; }
}
