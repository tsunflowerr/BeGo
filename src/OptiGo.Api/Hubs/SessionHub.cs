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
