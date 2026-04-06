using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Api.Hubs;

namespace OptiGo.Api.Services;

/// <summary>
/// Triển khai ISessionNotifier dùng ASP.NET Core SignalR.
/// Dùng IHubContext để push events từ bất kỳ service nào (không chỉ trong Hub).
/// Được inject vào các MediatR Handlers để gửi notification sau mỗi action.
/// </summary>
public class SignalRSessionNotifier : ISessionNotifier
{
    private readonly IHubContext<SessionHub> _hubContext;
    private readonly ILogger<SignalRSessionNotifier> _logger;

    public SignalRSessionNotifier(
        IHubContext<SessionHub> hubContext,
        ILogger<SignalRSessionNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task NotifyMemberJoinedAsync(
        Guid sessionId,
        Guid memberId,
        string memberName,
        int totalMembers,
        CancellationToken ct = default)
    {
        var group = SessionHub.GetGroupName(sessionId.ToString());
        _logger.LogDebug("→ SignalR [{Group}] MemberJoined: {MemberName} ({TotalMembers} total)",
            group, memberName, totalMembers);

        await _hubContext.Clients.Group(group).SendAsync("MemberJoined", new
        {
            sessionId,
            memberId,
            memberName,
            totalMembers
        }, ct);
    }

    /// <inheritdoc/>
    public async Task NotifyComputingStartedAsync(Guid sessionId, CancellationToken ct = default)
    {
        var group = SessionHub.GetGroupName(sessionId.ToString());
        _logger.LogDebug("→ SignalR [{Group}] ComputingStarted", group);

        await _hubContext.Clients.Group(group).SendAsync("ComputingStarted", new
        {
            sessionId,
            message = "Hệ thống đang tính toán điểm hẹn tối ưu...",
            timestamp = DateTime.UtcNow
        }, ct);
    }

    /// <inheritdoc/>
    public async Task NotifyOptimizationCompletedAsync(
        Guid sessionId,
        object result,
        CancellationToken ct = default)
    {
        var group = SessionHub.GetGroupName(sessionId.ToString());
        _logger.LogDebug("→ SignalR [{Group}] OptimizationCompleted", group);

        await _hubContext.Clients.Group(group).SendAsync("OptimizationCompleted", new
        {
            sessionId,
            result,
            timestamp = DateTime.UtcNow
        }, ct);
    }

    /// <inheritdoc/>
    public async Task NotifyVoteSubmittedAsync(
        Guid sessionId,
        Guid memberId,
        string venueId,
        int totalVotes,
        int totalMembers,
        CancellationToken ct = default)
    {
        var group = SessionHub.GetGroupName(sessionId.ToString());
        _logger.LogDebug("→ SignalR [{Group}] VoteSubmitted: {Votes}/{Total}",
            group, totalVotes, totalMembers);

        await _hubContext.Clients.Group(group).SendAsync("VoteSubmitted", new
        {
            sessionId,
            memberId,
            venueId,
            totalVotes,
            totalMembers,
            progress = totalMembers > 0
                ? Math.Round((double)totalVotes / totalMembers * 100, 0)
                : 0
        }, ct);
    }

    /// <inheritdoc/>
    public async Task NotifyVotingCompletedAsync(
        Guid sessionId,
        string winningVenueId,
        CancellationToken ct = default)
    {
        var group = SessionHub.GetGroupName(sessionId.ToString());
        _logger.LogDebug("→ SignalR [{Group}] VotingCompleted: Winner={VenueId}", group, winningVenueId);

        await _hubContext.Clients.Group(group).SendAsync("VotingCompleted", new
        {
            sessionId,
            winningVenueId,
            message = "Nhóm đã chọn được điểm hẹn!",
            timestamp = DateTime.UtcNow
        }, ct);
    }
}
