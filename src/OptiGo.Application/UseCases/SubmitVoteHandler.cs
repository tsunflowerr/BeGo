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
    private readonly ISessionNotifier _notifier;
    private readonly ILogger<SubmitVoteHandler> _logger;

    public SubmitVoteHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ILogger<SubmitVoteHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<SubmitVoteResult> Handle(SubmitVoteCommand request, CancellationToken cancellationToken)
    {

        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
        {
            return new SubmitVoteResult { IsSuccess = false, ErrorMessage = "Session not found." };
        }

        try
        {

            var vote = new Vote(request.SessionId, request.MemberId, request.VenueId);
            session.SubmitVote(vote);

            bool isCompleted = false;
            string? winningVenueId = null;

            if (session.AllMembersVoted())
            {
                _logger.LogInformation("All members voted in Session {SessionId}. Completing session.", session.Id);

                session.ChangeStatus(SessionStatus.Completed);
                isCompleted = true;

                winningVenueId = session.Votes
                    .GroupBy(v => v.VenueId)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;

                session.SetWinningVenue(winningVenueId);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _notifier.NotifyVoteSubmittedAsync(
                session.Id,
                request.MemberId,
                request.VenueId,
                session.Votes.Count,
                session.Members.Count,
                cancellationToken);

            if (isCompleted && winningVenueId != null)
            {

                await _notifier.NotifyVotingCompletedAsync(session.Id, winningVenueId, cancellationToken);
            }

            return new SubmitVoteResult
            {
                IsSuccess = true,
                IsVotingCompleted = isCompleted,
                WinningVenueId = winningVenueId
            };
        }
        catch (Exception ex)
        {

            _logger.LogWarning(ex, "Failed to submit vote for session {SessionId}, member {MemberId}", request.SessionId, request.MemberId);
            return new SubmitVoteResult { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }
}