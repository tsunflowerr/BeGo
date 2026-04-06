namespace OptiGo.Application.Interfaces;

public interface ISessionNotifier
{
    /// <summary>
    /// Thông báo có thành viên mới join session.
    /// </summary>
    Task NotifyMemberJoinedAsync(
        Guid sessionId,
        Guid memberId,
        string memberName,
        int totalMembers,
        CancellationToken ct = default);

    /// <summary>
    /// Thông báo hệ thống đang bắt đầu tính toán (bắt đầu computing state).
    /// </summary>
    Task NotifyComputingStartedAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Thông báo kết quả tính toán hoàn thành — push Top 3 venues lên client.
    /// Payload là JSON-serializable object để tránh coupling với DTO cụ thể.
    /// </summary>
    Task NotifyOptimizationCompletedAsync(Guid sessionId, object result, CancellationToken ct = default);

    /// <summary>
    /// Thông báo có vote mới được cast (broadcast cho tất cả trong session).
    /// </summary>
    Task NotifyVoteSubmittedAsync(
        Guid sessionId,
        Guid memberId,
        string venueId,
        int totalVotes,
        int totalMembers,
        CancellationToken ct = default);

    /// <summary>
    /// Thông báo voting hoàn thành — push winning venue.
    /// </summary>
    Task NotifyVotingCompletedAsync(Guid sessionId, string winningVenueId, CancellationToken ct = default);
}
