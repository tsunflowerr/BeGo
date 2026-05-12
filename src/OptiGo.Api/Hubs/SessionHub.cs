using Microsoft.AspNetCore.SignalR;
using OptiGo.Application.Interfaces;
using System.Security.Claims;

namespace OptiGo.Api.Hubs;

public class SessionHub : Hub
{
    private readonly ILogger<SessionHub> _logger;
    private readonly ISessionRepository _sessionRepository;

    public SessionHub(ILogger<SessionHub> logger, ISessionRepository sessionRepository)
    {
        _logger = logger;
        _sessionRepository = sessionRepository;
    }

    public async Task JoinSessionGroup(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "INVALID_SESSION_ID",
                message = "Session ID không hợp lệ."
            });
            return;
        }

        var subject = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = await _sessionRepository.GetByIdWithDetailsAsync(parsedSessionId);
        if (string.IsNullOrWhiteSpace(subject) ||
            session == null ||
            !session.Members.Any(member => member.IsOwnedBy(subject)))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "FORBIDDEN_SESSION_GROUP",
                message = "Bạn cần tham gia phòng trước khi kết nối realtime."
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
        if (!Guid.TryParse(sessionId, out var parsedSessionId) || !Guid.TryParse(memberId, out var parsedMemberId))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "INVALID_MEMBER_LEAVE",
                message = "Thông tin thành viên rời phòng không hợp lệ."
            });
            return;
        }

        var subject = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = await _sessionRepository.GetByIdWithDetailsAsync(parsedSessionId);
        var currentMember = string.IsNullOrWhiteSpace(subject)
            ? null
            : session?.Members.FirstOrDefault(member => member.IsOwnedBy(subject));
        var hostId = session?.Members.OrderBy(member => member.JoinedAt).FirstOrDefault()?.Id;
        if (session == null || currentMember == null || (currentMember.Id != parsedMemberId && currentMember.Id != hostId))
        {
            await Clients.Caller.SendAsync("Error", new
            {
                code = "FORBIDDEN_MEMBER_LEAVE",
                message = "Bạn không có quyền gửi sự kiện rời phòng cho thành viên này."
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

    public async Task SendChatMessage(string sessionId, string memberId, string text)
    {
        await Clients.Caller.SendAsync("Error", new
        {
            code = "CHAT_SEND_VIA_API",
            message = "Tin nhắn cần gửi qua API để được lưu và kiểm tra quyền."
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
