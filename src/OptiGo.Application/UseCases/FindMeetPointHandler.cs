using MediatR;
using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Services;
using OptiGo.Domain.ValueObjects;
using System.Text.Json;

namespace OptiGo.Application.UseCases;

public class FindMeetPointHandler : IRequestHandler<FindMeetPointCommand, FindMeetPointResult>
{
    private const double InitialVenueSearchRadiusMeters = 500;
    private const int DesiredNearbyVenueCount = 50;
    private const int FilteredVenueCount = 15;
    private const int FinalVenueCount = 3;
    private const int PlanningConcurrency = 3;

    private readonly ISessionRepository _sessionRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IPlacesProvider _placesProvider;
    private readonly IAIService _aiService;
    private readonly IOutingRoutePlanner _outingRoutePlanner;
    private readonly IVenuePrefilter _venuePrefilter;
    private readonly IVenueEvaluator _venueEvaluator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ILogger<FindMeetPointHandler> _logger;
    private readonly ICurrentUser _currentUser;

    public FindMeetPointHandler(
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        IPlacesProvider placesProvider,
        IAIService aiService,
        IOutingRoutePlanner outingRoutePlanner,
        IVenuePrefilter venuePrefilter,
        IVenueEvaluator venueEvaluator,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ILogger<FindMeetPointHandler> logger,
        ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _venueRepository = venueRepository;
        _placesProvider = placesProvider;
        _aiService = aiService;
        _outingRoutePlanner = outingRoutePlanner;
        _venuePrefilter = venuePrefilter;
        _venueEvaluator = venueEvaluator;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<FindMeetPointResult> Handle(FindMeetPointCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
        {
            return new FindMeetPointResult { IsSuccess = false, ErrorMessage = "Session not found." };
        }

        if (session.Members.Count == 0)
        {
            return new FindMeetPointResult { IsSuccess = false, ErrorMessage = "Session has no members." };
        }

        SessionAuthorization.RequireHost(session, _currentUser);

        session.ChangeStatus(SessionStatus.Computing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyComputingStartedAsync(session.Id, cancellationToken);

        try
        {
            var geometricMedian = OutingSearchCenterCalculator.Calculate(session);
            _logger.LogInformation(
                "Calculated weighted search center for Session {Id}: {Lat}, {Lng}",
                session.Id,
                geometricMedian.Latitude,
                geometricMedian.Longitude);

            var queryText = (!string.IsNullOrWhiteSpace(request.Category) && request.Category != "cafe")
                ? request.Category
                : (!string.IsNullOrWhiteSpace(session.QueryText) ? session.QueryText : "cafe");

            _logger.LogInformation(
                "Searching venues by natural language query '{Query}' at {Lat},{Lng}",
                queryText,
                geometricMedian.Latitude,
                geometricMedian.Longitude);

            var textVenues = await _placesProvider.SearchTextAsync(
                geometricMedian.Latitude,
                geometricMedian.Longitude,
                queryText,
                radiusMeters: InitialVenueSearchRadiusMeters,
                limit: DesiredNearbyVenueCount,
                cancellationToken);

            var rawVenues = textVenues;
            if (textVenues.Count < DesiredNearbyVenueCount)
            {
                _logger.LogInformation(
                    "Text search returned {Count}/{Target} venues for '{Query}'. Resolving broad fallback category.",
                    textVenues.Count,
                    DesiredNearbyVenueCount,
                    queryText);
                var category = await _aiService.ResolveCategoryAsync(queryText, cancellationToken);
                _logger.LogInformation("AI resolved fallback category for '{Query}' → '{Category}'", queryText, category);

                var fallbackVenues = await _placesProvider.SearchNearbyAsync(
                    geometricMedian.Latitude,
                    geometricMedian.Longitude,
                    category,
                    radiusMeters: InitialVenueSearchRadiusMeters,
                    limit: DesiredNearbyVenueCount,
                    cancellationToken);

                rawVenues = MergeTextFirst(textVenues, fallbackVenues, DesiredNearbyVenueCount);
            }

            if (rawVenues.Count == 0)
            {
                throw new InvalidOperationException("No venues found around the median point.");
            }

            var offlineFilteredVenues = CandidateFilter.FilterTopCandidates(
                session.Members.ToList(),
                rawVenues,
                FilteredVenueCount);

            _logger.LogInformation(
                "Offline filtered venues from {RawCount} to {FilteredCount} before route-aware Mapbox scoring.",
                rawVenues.Count,
                offlineFilteredVenues.Count);

            var filteredVenues = await _venuePrefilter.FilterTopCandidatesAsync(
                session,
                offlineFilteredVenues,
                topN: FilteredVenueCount,
                cancellationToken);
            var plannedCandidates = await PlanCandidatesAsync(session, filteredVenues, cancellationToken);

            var scoredCandidates = _venueEvaluator.RankCandidates(plannedCandidates, FinalVenueCount).ToList();
            var top3VenueHashes = new HashSet<string>(scoredCandidates.Select(s => s.VenueId));
            var top3VenueEntities = filteredVenues.Where(v => top3VenueHashes.Contains(v.Id)).ToList();

            await _venueRepository.AddRangeAsync(top3VenueEntities, cancellationToken);
            session.SetNominatedVenues(scoredCandidates.Select(s => s.VenueId));

            session.ChangeStatus(SessionStatus.Voting);
            var top3Result = await EnrichTop3VenuesAsync(scoredCandidates, top3VenueEntities, cancellationToken);

            var snapshot = new FindMeetPointResult
            {
                IsSuccess = true,
                GeometricMedian = geometricMedian,
                TopVenues = top3Result
            };
            session.SetLatestOptimizationSnapshot(JsonSerializer.Serialize(snapshot));
            session.SetFinalRouteSnapshot(null);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _notifier.NotifyOptimizationCompletedAsync(session.Id, snapshot, cancellationToken);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute meetpoint for session {Id}", session.Id);
            session.ChangeStatus(SessionStatus.Failed);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<List<CandidateResultDto>> EnrichTop3VenuesAsync(
        List<CandidateResultDto> top3Scores,
        List<Venue> top3VenueEntities,
        CancellationToken cancellationToken)
    {
        var results = new List<CandidateResultDto>();

        _logger.LogInformation("Fetching place details for {Count} top venues", top3VenueEntities.Count);
        var detailTasks = top3VenueEntities.Select(v => _placesProvider.GetPlaceDetailAsync(v.Id, cancellationToken)).ToList();
        var placeDetails = await Task.WhenAll(detailTasks);
        var detailsDict = placeDetails.ToDictionary(d => d.PlaceId, d => d);

        _logger.LogInformation("Generating AI summaries for top venues...");
        var summaryTasks = top3Scores.Select(async score => {
            var venue = top3VenueEntities.First(v => v.Id == score.VenueId);
            detailsDict.TryGetValue(venue.Id, out var placeDetail);

            if (string.IsNullOrEmpty(placeDetail?.AiReviewSummary))
            {
                var reviews = placeDetail?.Reviews.Select(r => r.Text) ?? new List<string>();
                return await _aiService.SummarizeReviewsAsync(reviews, cancellationToken);
            }
            return placeDetail.AiReviewSummary;
        }).ToList();

        var aiSummaries = await Task.WhenAll(summaryTasks);

        for (int i = 0; i < top3Scores.Count; i++)
        {
            var score = top3Scores[i];
            var venue = top3VenueEntities.First(v => v.Id == score.VenueId);

            detailsDict.TryGetValue(venue.Id, out var placeDetail);

            results.Add(new CandidateResultDto
            {
                VenueId = score.VenueId,
                Name = venue.Name,
                Category = venue.Category,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude,
                Address = venue.Address,
                Rating = venue.Rating,
                ReviewCount = venue.ReviewCount,
                TotalTimeSeconds = score.TotalTimeSeconds,
                FinalScore = score.FinalScore,
                MaxDriverDetourSeconds = score.MaxDriverDetourSeconds,
                TotalWalkingDistanceMeters = score.TotalWalkingDistanceMeters,
                IsFeasible = score.IsFeasible,
                FeasibilityIssues = score.FeasibilityIssues,
                RecommendationType = score.RecommendationType,
                OptimizationReason = score.OptimizationReason,
                TradeOffSummary = score.TradeOffSummary,
                Metrics = score.Metrics,
                ScoreBreakdown = score.ScoreBreakdown,
                MemberRoutes = score.MemberRoutes,
                DriverRoutes = score.DriverRoutes,
                PhotoUrls = placeDetail?.PhotoUrls ?? new List<string>(),
                AiReviewSummary = aiSummaries[i],
                TopReviews = placeDetail?.Reviews.Select(r => new ReviewDto
                {
                    AuthorName = r.AuthorName,
                    Rating = r.Rating,
                    Text = r.Text,
                    RelativeTime = r.RelativeTime
                }).ToList() ?? new List<ReviewDto>()
            });
        }

        return results;
    }

    private async Task<List<CandidateResultDto>> PlanCandidatesAsync(
        Session session,
        IReadOnlyList<Venue> venues,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(PlanningConcurrency);
        var tasks = venues.Select(async venue =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await _outingRoutePlanner.PlanVenueAsync(session, venue, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        return (await Task.WhenAll(tasks))
            .ToList();
    }

    private static IReadOnlyList<Venue> MergeTextFirst(
        IReadOnlyList<Venue> textVenues,
        IReadOnlyList<Venue> fallbackVenues,
        int limit)
    {
        var merged = new List<Venue>(Math.Min(limit, textVenues.Count + fallbackVenues.Count));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var venue in textVenues.Concat(fallbackVenues))
        {
            if (!seen.Add(venue.Id))
            {
                continue;
            }

            merged.Add(venue);
            if (merged.Count >= limit)
            {
                break;
            }
        }

        return merged;
    }
}
