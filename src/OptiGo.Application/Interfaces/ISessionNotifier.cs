namespace OptiGo.Application.Interfaces;

public interface ISessionNotifier
{

    Task NotifyMemberJoinedAsync(
        Guid sessionId,
        Guid memberId,
        string memberName,
        double latitude,
        double longitude,
        Domain.Enums.TransportMode transportMode,
        Domain.Enums.MemberMobilityRole mobilityRole,
        DateTime joinedAt,
        bool isHost,
        int totalMembers,
        CancellationToken ct = default);

    Task NotifyComputingStartedAsync(Guid sessionId, CancellationToken ct = default);

    Task NotifyOptimizationCompletedAsync(Guid sessionId, object result, CancellationToken ct = default);

    Task NotifyVoteSubmittedAsync(
        Guid sessionId,
        Guid memberId,
        string venueId,
        int totalVotes,
        int totalMembers,
        CancellationToken ct = default);

    Task NotifyVotingCompletedAsync(Guid sessionId, string winningVenueId, CancellationToken ct = default);

    Task NotifyPickupRequestsUpdatedAsync(Guid sessionId, CancellationToken ct = default);

    Task NotifyDepartureLockedAsync(Guid sessionId, CancellationToken ct = default);
}
