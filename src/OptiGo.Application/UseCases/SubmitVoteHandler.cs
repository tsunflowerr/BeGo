using MediatR;
using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OptiGo.Application.UseCases;

public class SubmitVoteHandler : IRequestHandler<SubmitVoteCommand, SubmitVoteResult>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SubmitVoteHandler> _logger;

    public SubmitVoteHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        ILogger<SubmitVoteHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SubmitVoteResult> Handle(SubmitVoteCommand request, CancellationToken cancellationToken)
    {
        // 1. Lấy Session cùng toàn bộ Members và Votes
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
        {
            return new SubmitVoteResult { IsSuccess = false, ErrorMessage = "Session not found." };
        }

        try
        {
            // 2. Tạo phiếu bầu và thêm vào Session (Aggregate Root sẽ tự kiểm tra Domain logic)
            var vote = new Vote(request.SessionId, request.MemberId, request.VenueId);
            session.SubmitVote(vote);

            bool isCompleted = false;
            string? winningVenueId = null;

            // 3. Nếu mọi người đều đã vote
            if (session.AllMembersVoted())
            {
                _logger.LogInformation("All members voted in Session {SessionId}. Completing session.", session.Id);
                
                // Đổi trạng thái sang Completed
                session.ChangeStatus(SessionStatus.Completed);
                isCompleted = true;

                // Tìm quán có nhiều phiếu bầu nhất (Trường hợp hòa: Lấy quán bị vote sớm nhất hoặc ngẫu nhiên)
                winningVenueId = session.Votes
                    .GroupBy(v => v.VenueId)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;
            }

            // 4. Lưu lại vào Database
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new SubmitVoteResult
            {
                IsSuccess = true,
                IsVotingCompleted = isCompleted,
                WinningVenueId = winningVenueId
            };
        }
        catch (Exception ex)
        {
            // Bắt DomainException (Ví dụ: Member đã vote rồi, hoặc session không ở trạng thái Voting)
            _logger.LogWarning(ex, "Failed to submit vote for session {SessionId}, member {MemberId}", request.SessionId, request.MemberId);
            return new SubmitVoteResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }
}