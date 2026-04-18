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
    private const int FilteredVenueCount = 25;
    private const int FinalVenueCount = 3;

    private readonly ISessionRepository _sessionRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IPlacesProvider _placesProvider;
    private readonly ITravelTimeService _travelTimeService;
    private readonly IAIService _aiService;
    private readonly IOutingRoutePlanner _outingRoutePlanner;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ILogger<FindMeetPointHandler> _logger;

    public FindMeetPointHandler(
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        IPlacesProvider placesProvider,
        ITravelTimeService travelTimeService,
        IAIService aiService,
        IOutingRoutePlanner outingRoutePlanner,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ILogger<FindMeetPointHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _venueRepository = venueRepository;
        _placesProvider = placesProvider;
        _travelTimeService = travelTimeService;
        _aiService = aiService;
        _outingRoutePlanner = outingRoutePlanner;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _logger = logger;
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

        if (session.HasPendingPickupRequests())
        {
            return new FindMeetPointResult
            {
                IsSuccess = false,
                ErrorMessage = "Please resolve all pickup requests before optimizing venues."
            };
        }

        session.ChangeStatus(SessionStatus.Computing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyComputingStartedAsync(session.Id, cancellationToken);

        try
        {
            var membersList = session.Members.ToList();
            var geometricMedian = WeightedGeometricMedianCalculator.Calculate(membersList);
            _logger.LogInformation(
                "Calculated weighted search center for Session {Id}: {Lat}, {Lng}",
                session.Id,
                geometricMedian.Latitude,
                geometricMedian.Longitude);

            var queryText = (!string.IsNullOrWhiteSpace(request.Category) && request.Category != "cafe")
                ? request.Category
                : (!string.IsNullOrWhiteSpace(session.QueryText) ? session.QueryText : "cafe");

            _logger.LogInformation("Starting AI resolution for query: '{Query}'", queryText);
            var category = await _aiService.ResolveCategoryAsync(queryText, cancellationToken);
            _logger.LogInformation("AI resolved query '{Query}' → category '{Category}'", queryText, category);

            _logger.LogInformation("Searching venues for category '{Category}' at {Lat},{Lng}", category, geometricMedian.Latitude, geometricMedian.Longitude);
            var rawVenues = await _placesProvider.SearchNearbyAsync(
                geometricMedian.Latitude,
                geometricMedian.Longitude,
                category,
                radiusMeters: InitialVenueSearchRadiusMeters,
                limit: DesiredNearbyVenueCount,
                cancellationToken);

            if (rawVenues.Count == 0)
            {
                throw new InvalidOperationException("No venues found around the median point.");
            }

            var filteredVenues = CandidateFilter.FilterTopCandidates(
                membersList,
                rawVenues,
                topN: FilteredVenueCount);
            var plannedCandidates = new List<CandidateResultDto>();
            foreach (var venue in filteredVenues)
            {
                plannedCandidates.Add(await _outingRoutePlanner.PlanVenueAsync(session, venue, cancellationToken));
            }

            var scoredCandidates = ScoreCandidates(plannedCandidates).Take(FinalVenueCount).ToList();
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
            return new FindMeetPointResult { IsSuccess = false, ErrorMessage = ex.Message };
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

    private static List<CandidateResultDto> ScoreCandidates(IReadOnlyList<CandidateResultDto> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var stats = candidates.Select(candidate =>
        {
            var times = candidate.MemberRoutes.Select(route => route.EstimatedTimeSeconds).ToList();
            var meanTime = times.Count == 0 ? 0 : times.Average();
            var stdDev = times.Count == 0
                ? 0
                : Math.Sqrt(times.Sum(time => Math.Pow(time - meanTime, 2)) / times.Count);

            return new
            {
                Candidate = candidate,
                candidate.TotalTimeSeconds,
                Fairness = stdDev,
                candidate.MaxDriverDetourSeconds,
                candidate.TotalWalkingDistanceMeters,
                QualityPenalty = candidate.Rating > 0 ? 5.0 - candidate.Rating : 2.0
            };
        }).ToList();

        var minTotal = stats.Min(x => x.TotalTimeSeconds);
        var maxTotal = stats.Max(x => x.TotalTimeSeconds);
        var minFairness = stats.Min(x => x.Fairness);
        var maxFairness = stats.Max(x => x.Fairness);
        var minDetour = stats.Min(x => x.MaxDriverDetourSeconds);
        var maxDetour = stats.Max(x => x.MaxDriverDetourSeconds);
        var minWalking = stats.Min(x => x.TotalWalkingDistanceMeters);
        var maxWalking = stats.Max(x => x.TotalWalkingDistanceMeters);
        var minQuality = stats.Min(x => x.QualityPenalty);
        var maxQuality = stats.Max(x => x.QualityPenalty);

        foreach (var stat in stats)
        {
            var normalizedTotal = Normalize(stat.TotalTimeSeconds, minTotal, maxTotal);
            var normalizedFairness = Normalize(stat.Fairness, minFairness, maxFairness);
            var normalizedDetour = Normalize(stat.MaxDriverDetourSeconds, minDetour, maxDetour);
            var normalizedWalking = Normalize(stat.TotalWalkingDistanceMeters, minWalking, maxWalking);
            var normalizedQuality = Normalize(stat.QualityPenalty, minQuality, maxQuality);

            stat.Candidate.FinalScore =
                normalizedTotal * 0.45 +
                normalizedDetour * 0.20 +
                normalizedFairness * 0.15 +
                normalizedWalking * 0.10 +
                normalizedQuality * 0.10;
        }

        return stats
            .Select(x => x.Candidate)
            .OrderBy(candidate => candidate.FinalScore)
            .ToList();
    }

    private static double Normalize(double value, double min, double max)
    {
        if (Math.Abs(max - min) < 0.0001)
            return 0;

        return (value - min) / (max - min);
    }
}
