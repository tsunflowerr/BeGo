using MediatR;
using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Services;
using OptiGo.Domain.ValueObjects;

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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ILogger<FindMeetPointHandler> _logger;

    public FindMeetPointHandler(
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        IPlacesProvider placesProvider,
        ITravelTimeService travelTimeService,
        IAIService aiService,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ILogger<FindMeetPointHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _venueRepository = venueRepository;
        _placesProvider = placesProvider;
        _travelTimeService = travelTimeService;
        _aiService = aiService;
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

        session.ChangeStatus(SessionStatus.Computing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyComputingStartedAsync(session.Id, cancellationToken);

        try
        {
            var membersList = session.Members.ToList();
            PickupPairValidator.ValidateOneToOnePairs(membersList);

            var memberIndexById = membersList
                .Select((member, index) => new { member.Id, Index = index })
                .ToDictionary(item => item.Id, item => item.Index);
            var passengerByDriverId = membersList
                .Where(member => member.DriverId.HasValue)
                .ToDictionary(member => member.DriverId!.Value, member => member);
            var driverIdByPassengerId = membersList
                .Where(member => member.DriverId.HasValue)
                .ToDictionary(member => member.Id, member => member.DriverId!.Value);

            var geometricMedian = WeightedGeometricMedianCalculator.Calculate(membersList);
            _logger.LogInformation(
                "Calculated weighted search center for Session {Id}: {Lat}, {Lng}",
                session.Id,
                geometricMedian.Latitude,
                geometricMedian.Longitude);

            var driverRoutes = new Dictionary<Guid, RouteResult>();
            foreach (var pair in passengerByDriverId)
            {
                var driver = membersList[memberIndexById[pair.Key]];
                var passenger = pair.Value;

                _logger.LogInformation(
                    "Calculating pickup pre-route for driver {Driver} to passenger {Passenger}",
                    driver.Name,
                    passenger.Name);

                var route = await _travelTimeService.GetRouteAsync(
                    driver.GetLocation(),
                    passenger.GetLocation(),
                    driver.TransportMode,
                    cancellationToken);

                driverRoutes[driver.Id] = route;
            }

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
            var venueLocations = filteredVenues.Select(v => v.GetLocation()).ToList();

            var durationMatrix = new double[membersList.Count, venueLocations.Count];
            var distanceMatrix = new double[membersList.Count, venueLocations.Count];

            var phaseOneGroups = membersList
                .Select((member, index) => new { Member = member, Index = index })
                .Where(item => !item.Member.DriverId.HasValue)
                .Select(item =>
                {
                    passengerByDriverId.TryGetValue(item.Member.Id, out var passenger);
                    var preRoute = passenger != null ? driverRoutes[item.Member.Id] : null;

                    return new
                    {
                        item.Member,
                        item.Index,
                        EffectiveMode = item.Member.TransportMode,
                        EffectiveOrigin = passenger != null ? passenger.GetLocation() : item.Member.GetLocation(),
                        ExtraDurationSeconds = preRoute?.DurationSeconds ?? 0.0,
                        ExtraDistanceMeters = preRoute?.DistanceMeters ?? 0.0
                    };
                })
                .GroupBy(x => x.EffectiveMode);

            foreach (var group in phaseOneGroups)
            {
                var groupOrigins = group.Select(x => x.EffectiveOrigin).ToList();
                var matrixResult = await _travelTimeService.GetTravelMatrixAsync(
                    groupOrigins, venueLocations, group.Key, cancellationToken);

                int groupRow = 0;
                foreach (var item in group)
                {
                    for (int v = 0; v < venueLocations.Count; v++)
                    {
                        durationMatrix[item.Index, v] = matrixResult.Durations[groupRow, v] + item.ExtraDurationSeconds;
                        distanceMatrix[item.Index, v] = matrixResult.Distances[groupRow, v] + item.ExtraDistanceMeters;
                    }
                    groupRow++;
                }
            }

            foreach (var passengerEntry in driverIdByPassengerId)
            {
                var passengerIndex = memberIndexById[passengerEntry.Key];
                var driverIndex = memberIndexById[passengerEntry.Value];

                for (int v = 0; v < venueLocations.Count; v++)
                {
                    durationMatrix[passengerIndex, v] = durationMatrix[driverIndex, v];
                    distanceMatrix[passengerIndex, v] = distanceMatrix[driverIndex, v];
                }
            }

            var currentWeights = ScoringWeights.Default;
            var topScoredVenues = ScoringEngine.CalculateScores(
                filteredVenues,
                durationMatrix,
                currentWeights,
                membersList.Count);

            var top3Scores = topScoredVenues.Take(FinalVenueCount).ToList();
            var top3VenueHashes = new HashSet<string>(top3Scores.Select(s => s.VenueId));
            var top3VenueEntities = filteredVenues.Where(v => top3VenueHashes.Contains(v.Id)).ToList();

            await _venueRepository.AddRangeAsync(top3VenueEntities, cancellationToken);
            session.SetNominatedVenues(top3Scores.Select(s => s.VenueId));

            session.ChangeStatus(SessionStatus.Voting);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var top3Result = await EnrichTop3VenuesAsync(
                top3Scores, top3VenueEntities, filteredVenues, membersList,
                durationMatrix, distanceMatrix, cancellationToken);

            await _notifier.NotifyOptimizationCompletedAsync(session.Id, top3Result, cancellationToken);

            return new FindMeetPointResult
            {
                IsSuccess = true,
                GeometricMedian = geometricMedian,
                TopVenues = top3Result
            };
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
        List<CandidateScore> top3Scores,
        List<Venue> top3VenueEntities,
        IReadOnlyList<Venue> filteredVenues,
        List<Member> membersList,
        double[,] durationMatrix,
        double[,] distanceMatrix,
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

            int venueIndex = -1;
            for (int j = 0; j < filteredVenues.Count; j++)
            {
                if (filteredVenues[j].Id == venue.Id)
                {
                    venueIndex = j;
                    break;
                }
            }

            if (venueIndex == -1) continue;

            detailsDict.TryGetValue(venue.Id, out var placeDetail);

            var memberRoutes = membersList.Select((member, memberIdx) => new MemberRouteDto
            {
                MemberId = member.Id,
                MemberName = member.Name,
                EstimatedTimeSeconds = durationMatrix[memberIdx, venueIndex],
                DistanceMeters = distanceMatrix[memberIdx, venueIndex]
            }).ToList();

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
                MemberRoutes = memberRoutes,
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
}
