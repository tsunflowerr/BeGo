using MediatR;
using System;

namespace OptiGo.Application.UseCases;

public record SubmitVoteCommand(Guid SessionId, Guid MemberId, string VenueId) : IRequest<SubmitVoteResult>;

public class SubmitVoteResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Đánh dấu xem session đã thu thập đủ phiếu bầu chưa
    public bool IsVotingCompleted { get; set; }
    
    // Nếu hoàn tất, trả về ID của quán cà phê/nhà hàng thắng cuộc
    public string? WinningVenueId { get; set; }
}
