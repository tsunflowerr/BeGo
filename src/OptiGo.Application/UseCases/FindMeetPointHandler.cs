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
    private readonly ISessionRepository _sessionRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IPlacesProvider _placesProvider;
    private readonly ITravelTimeService _travelTimeService;
    private readonly IAIService _aiService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FindMeetPointHandler> _logger;

    public FindMeetPointHandler(
        ISessionRepository sessionRepository,
        IVenueRepository venueRepository,
        IPlacesProvider placesProvider,
        ITravelTimeService travelTimeService,
        IAIService aiService,
        IUnitOfWork unitOfWork,
        ILogger<FindMeetPointHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _venueRepository = venueRepository;
        _placesProvider = placesProvider;
        _travelTimeService = travelTimeService;
        _aiService = aiService;
        _unitOfWork = unitOfWork;
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

        try
        {
            var memberLocations = session.Members.Select(m => m.GetLocation()).ToList();

            // 3. Tính Geometric Median (Weiszfeld) - Tìm điểm trung tâm để quy hoạch Bounding Box
            var geometricMedian = GeometricMedianCalculator.Calculate(memberLocations);
            _logger.LogInformation("Calculated Geometric Median for Session {Id}: {Lat}, {Lng}", 
                session.Id, geometricMedian.Latitude, geometricMedian.Longitude);

            // 4. AI phân tích ngôn ngữ tự nhiên → Google Places category
            // QueryText có thể từ Session hoặc từ request, ưu tiên theo thứ tự: request.Category → session.QueryText → "cafe"
            var queryText = !string.IsNullOrWhiteSpace(request.Category) 
                ? request.Category 
                : session.QueryText ?? "cafe";

            var category = await _aiService.ResolveCategoryAsync(queryText, cancellationToken);
            _logger.LogInformation("AI resolved query '{Query}' → category '{Category}'", queryText, category);

            // 5. Tìm kiếm các Venues tiềm năng dùng Places Provider trong 3km
            var rawVenues = await _placesProvider.SearchNearbyAsync(
                geometricMedian.Latitude, geometricMedian.Longitude, category, radiusMeters: 3000, limit: 50, cancellationToken);

            if (rawVenues.Count == 0)
            {
                throw new InvalidOperationException("No venues found around the median point.");
            }

            // 5. Pre-filter (Loại bỏ các quán mạn ngoài viền bằng Haversine, giữ lại Top 25 để tránh sập Matrix API)
            var filteredVenues = CandidateFilter.FilterTopCandidates(session.Members.ToList(), rawVenues, topN: 25);
            var venueLocations = filteredVenues.Select(v => v.GetLocation()).ToList();

            // 6. Tính Travel Time Matrix qua external API (Mapbox)
            // Chia cụm (Batching) theo Transport Mode của từng Member để tránh sai lệch thời gian di chuyển
            var membersList = session.Members.ToList();
            var matrix = new double[membersList.Count, venueLocations.Count];

            var modeGroups = membersList
                .Select((m, index) => new { Member = m, Index = index })
                .GroupBy(x => x.Member.TransportMode);

            foreach (var group in modeGroups)
            {
                var groupOrigins = group.Select(x => x.Member.GetLocation()).ToList();
                
                // Gọi Matrix API cho phương tiện cụ thể (VD: Walking, Bus, Motorbike)
                var groupMatrix = await _travelTimeService.GetTravelTimeMatrixAsync(
                    groupOrigins, venueLocations, group.Key, cancellationToken);
                    
                // Ghép mảng kết quả vào ma trận tổng (theo đúng Index của Member trong cơ sở dữ liệu)
                int groupRow = 0;
                foreach (var item in group)
                {
                    for (int v = 0; v < venueLocations.Count; v++)
                    {
                        matrix[item.Index, v] = groupMatrix[groupRow, v];
                    }
                    groupRow++;
                }
            }

            // 7. Scoring (Min-Max Normalization đa mục tiêu)
            var currentWeights = ScoringWeights.Default; // Có thể tải từ DB theo cài đặt Session
            var topScoredVenues = ScoringEngine.CalculateScores(filteredVenues, matrix, currentWeights, membersList.Count);

            // Trích xuất Top 3
            var top3Scores = topScoredVenues.Take(3).ToList();
            var top3VenueHashes = new HashSet<string>(top3Scores.Select(s => s.VenueId));
            var top3VenueEntities = filteredVenues.Where(v => top3VenueHashes.Contains(v.Id)).ToList();

            // 8. Lưu các Venues thắng cuộc vào Database để làm Cache và đánh dấu đề cử
            await _venueRepository.AddRangeAsync(top3VenueEntities, cancellationToken);
            session.SetNominatedVenues(top3Scores.Select(s => s.VenueId));

            // 9. Chuyển trạng thái sang Voting
            session.ChangeStatus(SessionStatus.Voting);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 10. Map CandidateResultDto chứa Tên quán/Địa chỉ cho Frontend
            var top3Result = top3Scores.Select(score => 
            {
                var venue = top3VenueEntities.First(v => v.Id == score.VenueId);
                return new CandidateResultDto
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
                    FinalScore = score.FinalScore
                };
            }).ToList();

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
}
