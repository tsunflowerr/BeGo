using MediatR;
using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OptiGo.Application.UseCases;

public class SubmitVoteHandler : IRequestHandler<SubmitVoteCommand, SubmitVoteResult>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IOutingRoutePlanner _outingRoutePlanner;
    private readonly IBaselineOutingRoutePlanner _baselineOutingRoutePlanner;
    private readonly IRouteBenchmarkRecorder _routeBenchmarkRecorder;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ILogger<SubmitVoteHandler> _logger;

    public SubmitVoteHandler(
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        IOutingRoutePlanner outingRoutePlanner,
        IBaselineOutingRoutePlanner baselineOutingRoutePlanner,
        IRouteBenchmarkRecorder routeBenchmarkRecorder,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ILogger<SubmitVoteHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _venueRepository = venueRepository;
        _outingRoutePlanner = outingRoutePlanner;
        _baselineOutingRoutePlanner = baselineOutingRoutePlanner;
        _routeBenchmarkRecorder = routeBenchmarkRecorder;
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

                winningVenueId = session.Votes
                    .GroupBy(v => v.VenueId)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .Key;

                var winningVenue = await _venueRepository.GetByIdAsync(winningVenueId, cancellationToken);
                if (winningVenue == null)
                    throw new InvalidOperationException("Winning venue could not be loaded.");

                var finalRoutePreview = await _outingRoutePlanner.PlanVenueAsync(session, winningVenue, cancellationToken);
                var baselineRoutePreview = await _baselineOutingRoutePlanner.PlanVenueAsync(session, winningVenue, cancellationToken);
                var baselineCost = baselineRoutePreview.ScoreBreakdown.GeneralizedCostSeconds;
                finalRoutePreview.BenchmarkComparison = new PlannerBenchmarkComparisonDto
                {
                    BaselinePlannerVersion = baselineRoutePreview.PlannerVersion,
                    ImprovedPlannerVersion = finalRoutePreview.PlannerVersion,
                    BaselineGeneralizedCostSeconds = baselineCost,
                    ImprovedGeneralizedCostSeconds = finalRoutePreview.ScoreBreakdown.GeneralizedCostSeconds,
                    ImprovementPercent = baselineCost <= 0
                        ? 0
                        : Math.Round((baselineCost - finalRoutePreview.ScoreBreakdown.GeneralizedCostSeconds) / baselineCost * 100, 2),
                    BaselineStopCount = baselineRoutePreview.DriverRoutes.Sum(route => route.Stops.Count(stop => stop.StopType.StartsWith("pickup", StringComparison.Ordinal))),
                    ImprovedStopCount = finalRoutePreview.DriverRoutes.Sum(route => route.Stops.Count(stop => stop.StopType.StartsWith("pickup", StringComparison.Ordinal)))
                };
                await _routeBenchmarkRecorder.RecordComparisonAsync(session.Id, finalRoutePreview, baselineRoutePreview, cancellationToken);

                session.ChangeStatus(SessionStatus.RoutePreview);
                session.SetFinalRouteSnapshot(JsonSerializer.Serialize(finalRoutePreview));
                session.SetWinningVenue(winningVenueId);
                isCompleted = true;
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
