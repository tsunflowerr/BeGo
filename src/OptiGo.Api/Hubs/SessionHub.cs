using Microsoft.AspNetCore.SignalR;

namespace OptiGo.Api.Hubs;

public class SessionHub : Hub
{
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(ILogger<SessionHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinSessionGroup(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out _))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "INVALID_SESSION_ID",
                message = "Session ID không hợp lệ."
            });
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(sessionId));
        _logger.LogInformation("Connection {ConnectionId} joined session group {SessionId}",
            Context.ConnectionId, sessionId);

        await Clients.Caller.SendAsync("JoinedSession", new
        {
            sessionId,
            connectionId = Context.ConnectionId,
            message = "Bạn đã kết nối thành công vào phiên."
        });
    }

    public async Task LeaveSessionGroup(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(sessionId));
        _logger.LogInformation("Connection {ConnectionId} left session group {SessionId}",
            Context.ConnectionId, sessionId);
    }

    public async Task NotifyMemberLeft(string sessionId, string memberId, string memberName, bool isHost)
    {
        if (!Guid.TryParse(sessionId, out _) || !Guid.TryParse(memberId, out _))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "INVALID_MEMBER_LEAVE",
                message = "Thông tin thành viên rời phòng không hợp lệ."
            });
            return;
        }

        _logger.LogInformation("Member {MemberName} left session {SessionId}. IsHost={IsHost}",
            memberName, sessionId, isHost);

        await Clients.Group(GetGroupName(sessionId)).SendAsync("MemberLeft", new
        {
            sessionId,
            memberId,
            memberName,
            isHost,
            timestamp = DateTime.UtcNow
        });
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("SignalR client disconnected: {ConnectionId}. Reason: {Reason}",
            Context.ConnectionId, exception?.Message ?? "Normal disconnect");
        return base.OnDisconnectedAsync(exception);
    }

    public static string GetGroupName(string sessionId) => $"session-{sessionId}";
}
