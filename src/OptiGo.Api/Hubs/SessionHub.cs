using Microsoft.AspNetCore.SignalR;

namespace OptiGo.Api.Hubs;

/// <summary>
/// SignalR Hub cho realtime communication của một phiên OptiGo.
/// Mỗi session có 1 Group riêng biệt được đặt tên theo SessionId.
/// </summary>
/// <remarks>
/// === Client Events (Server → Client) ===
///
/// - "MemberJoined"         : Có thành viên mới join session
/// - "ComputingStarted"     : Hệ thống bắt đầu tính toán
/// - "OptimizationCompleted": Kết quả Top 3 venues đã sẵn sàng
/// - "VoteSubmitted"        : Có vote mới (cập nhật UI số phiếu)
/// - "VotingCompleted"      : Voting đóng lại, có quán thắng
/// - "Error"                : Thông báo lỗi về client
///
/// === Client-to-Server Methods (Client → Server) ===
///
/// - JoinSessionGroup(sessionId): Client subscribe để nhận events của session
/// - LeaveSessionGroup(sessionId): Client unsubscribe
/// </remarks>
public class SessionHub : Hub
{
    private readonly ILogger<SessionHub> _logger;

    public SessionHub(ILogger<SessionHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client gọi method này để subscribe vào nhóm của một session cụ thể.
    /// Sau khi join, client sẽ nhận được tất cả events của session đó.
    /// </summary>
    /// <param name="sessionId">ID của session cần theo dõi</param>
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

        // Xác nhận đã join thành công
        await Clients.Caller.SendAsync("JoinedSession", new 
        { 
            sessionId,
            connectionId = Context.ConnectionId,
            message = "Bạn đã kết nối thành công vào phiên."
        });
    }

    /// <summary>
    /// Client rời khỏi nhóm session (ví dụ: đóng tab, điều hướng sang trang khác).
    /// </summary>
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

    /// <summary>
    /// Tạo tên group theo convention: "session-{sessionId}"
    /// Dùng static để cả Hub và Notifier dùng cùng 1 tên.
    /// </summary>
    public static string GetGroupName(string sessionId) => $"session-{sessionId}";
}
