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
        // 1. Lấy thông tin Session và Members
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
        {
            return new FindMeetPointResult { IsSuccess = false, ErrorMessage = "Session not found." };
        }

        if (session.Members.Count == 0)
        {
            return new FindMeetPointResult { IsSuccess = false, ErrorMessage = "Session has no members." };
        }

        // 2. Cập nhật state (Đang tính toán)
        session.ChangeStatus(SessionStatus.Computing);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // SignalR: Notify computing started
        await _notifier.NotifyComputingStartedAsync(session.Id, cancellationToken);

        try
        {
            var memberLocations = session.Members.Select(m => m.GetLocation()).ToList();

            // 3. Tính Geometric Median (Weiszfeld) - Tìm điểm trung tâm để quy hoạch Bounding Box
            var geometricMedian = GeometricMedianCalculator.Calculate(memberLocations);
            _logger.LogInformation("Calculated Geometric Median for Session {Id}: {Lat}, {Lng}", 
                session.Id, geometricMedian.Latitude, geometricMedian.Longitude);

            // 4. AI phân tích ngôn ngữ tự nhiên → Google Places category
            var queryText = (!string.IsNullOrWhiteSpace(request.Category) && request.Category != "cafe")
                ? request.Category 
                : (!string.IsNullOrWhiteSpace(session.QueryText) ? session.QueryText : "cafe");

            _logger.LogInformation("Starting AI resolution for query: '{Query}'", queryText);
            var category = await _aiService.ResolveCategoryAsync(queryText, cancellationToken);
            _logger.LogInformation("AI resolved query '{Query}' → category '{Category}'", queryText, category);

            // 5. Tìm kiếm pool venue đủ rộng cho bước pre-filter.
            // Provider sẽ tự mở rộng bán kính và gộp kết quả để tiến tới ~50 candidates.
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

            // 6. Pre-filter (Loại bỏ venue quá xa, giữ lại một tập vừa đủ để gọi Matrix API hiệu quả)
            var filteredVenues = CandidateFilter.FilterTopCandidates(
                session.Members.ToList(),
                rawVenues,
                topN: FilteredVenueCount);
            var venueLocations = filteredVenues.Select(v => v.GetLocation()).ToList();

            // 7. Tính Travel Time + Distance Matrix qua external API (Mapbox/Google)
            // Chia cụm (Batching) theo Transport Mode của từng Member để tránh sai lệch thời gian di chuyển
            var membersList = session.Members.ToList();
            var durationMatrix = new double[membersList.Count, venueLocations.Count];
            var distanceMatrix = new double[membersList.Count, venueLocations.Count];

            var modeGroups = membersList
                .Select((m, index) => new { Member = m, Index = index })
                .GroupBy(x => x.Member.TransportMode);

            foreach (var group in modeGroups)
            {
                var groupOrigins = group.Select(x => x.Member.GetLocation()).ToList();
                
                // Gọi Matrix API cho phương tiện cụ thể - lấy cả duration và distance trong 1 request
                var matrixResult = await _travelTimeService.GetTravelMatrixAsync(
                    groupOrigins, venueLocations, group.Key, cancellationToken);
                    
                // Ghép mảng kết quả vào ma trận tổng (theo đúng Index của Member trong cơ sở dữ liệu)
                int groupRow = 0;
                foreach (var item in group)
                {
                    for (int v = 0; v < venueLocations.Count; v++)
                    {
                        durationMatrix[item.Index, v] = matrixResult.Durations[groupRow, v];
                        distanceMatrix[item.Index, v] = matrixResult.Distances[groupRow, v];
                    }
                    groupRow++;
                }
            }

            // 8. Scoring (Min-Max Normalization đa mục tiêu)
            var currentWeights = ScoringWeights.Default; // Có thể tải từ DB theo cài đặt Session
            var topScoredVenues = ScoringEngine.CalculateScores(filteredVenues, durationMatrix, currentWeights, membersList.Count);

            // Trích xuất Top N venue cuối cùng
            var top3Scores = topScoredVenues.Take(FinalVenueCount).ToList();
            var top3VenueHashes = new HashSet<string>(top3Scores.Select(s => s.VenueId));
            var top3VenueEntities = filteredVenues.Where(v => top3VenueHashes.Contains(v.Id)).ToList();

            // 9. Lưu các venue đề cử vào Database để làm cache
            await _venueRepository.AddRangeAsync(top3VenueEntities, cancellationToken);
            session.SetNominatedVenues(top3Scores.Select(s => s.VenueId));

            // 10. Chuyển trạng thái sang Voting
            session.ChangeStatus(SessionStatus.Voting);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 11. Lấy thông tin chi tiết cho Top venues cuối cùng (photos, reviews, AI summary)
            // Distance đã có sẵn từ Matrix API - không cần gọi thêm API nào
            var top3Result = await EnrichTop3VenuesAsync(
                top3Scores, top3VenueEntities, filteredVenues, membersList, 
                durationMatrix, distanceMatrix, cancellationToken);

            // SignalR: Notify optimization completed
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

    /// <summary>
    /// Enrich Top 3 venues với thông tin chi tiết từ Google Places Detail API.
    /// Distance đã được lấy từ Matrix API - không cần gọi thêm Directions API.
    /// </summary>
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

        // 1. Parallel fetch place details từ Google cho tất cả top venues
        _logger.LogInformation("Fetching place details for {Count} top venues", top3VenueEntities.Count);
        var detailTasks = top3VenueEntities.Select(v => _placesProvider.GetPlaceDetailAsync(v.Id, cancellationToken)).ToList();
        var placeDetails = await Task.WhenAll(detailTasks);
        var detailsDict = placeDetails.ToDictionary(d => d.PlaceId, d => d);

        // 2. Parallel fetch AI summaries từ Groq (hoặc dùng từ Google nếu có)
        _logger.LogInformation("Generating AI summaries for top venues...");
        var summaryTasks = top3Scores.Select(async score => {
            var venue = top3VenueEntities.First(v => v.Id == score.VenueId);
            detailsDict.TryGetValue(venue.Id, out var placeDetail);
            
            // Nếu Google không có tóm tắt AI, dùng Groq để tóm tắt 20 review
            if (string.IsNullOrEmpty(placeDetail?.AiReviewSummary))
            {
                var reviews = placeDetail?.Reviews.Select(r => r.Text) ?? new List<string>();
                return await _aiService.SummarizeReviewsAsync(reviews, cancellationToken);
            }
            return placeDetail.AiReviewSummary;
        }).ToList();
        
        var aiSummaries = await Task.WhenAll(summaryTasks);

        // 3. Build result DTOs
        for (int i = 0; i < top3Scores.Count; i++)
        {
            var score = top3Scores[i];
            var venue = top3VenueEntities.First(v => v.Id == score.VenueId);
            
            // Tìm index của venue trong filteredVenues
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
