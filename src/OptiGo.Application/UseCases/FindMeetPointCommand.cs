using MediatR;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.UseCases;

public record FindMeetPointCommand(Guid SessionId, string Category = "cafe") : IRequest<FindMeetPointResult>;

public class FindMeetPointResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public Coordinate? GeometricMedian { get; set; }
    public List<CandidateResultDto>? TopVenues { get; set; }
}

public class CandidateResultDto
{
    public string VenueId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public double TotalTimeSeconds { get; set; }
    public double FinalScore { get; set; }
    public double MaxDriverDetourSeconds { get; set; }
    public double TotalWalkingDistanceMeters { get; set; }
    public string PlannerVersion { get; set; } = string.Empty;
    public double ApiCostEstimate { get; set; }
    public double CacheHitRatio { get; set; }
    public RouteScoreBreakdownDto ScoreBreakdown { get; set; } = new();
    public PlannerBenchmarkComparisonDto? BenchmarkComparison { get; set; }
    public CacheDiagnosticsDto? CacheDiagnostics { get; set; }
    public List<string> PhotoUrls { get; set; } = new();
    public string? AiReviewSummary { get; set; }
    public List<ReviewDto> TopReviews { get; set; } = new();
    public List<MemberRouteDto> MemberRoutes { get; set; } = new();
    public List<DriverRouteDto> DriverRoutes { get; set; } = new();
}
public class ReviewDto
{
    public string AuthorName { get; set; } = null!;
    public double Rating { get; set; }
    public string Text { get; set; } = null!;
    public string? RelativeTime { get; set; }
}

public class MemberRouteDto
{
    public Guid MemberId { get; set; }
    public string MemberName { get; set; } = null!;
    public double EstimatedTimeSeconds { get; set; }
    public double DistanceMeters { get; set; }
    public Guid? DriverId { get; set; }
    public double WalkingDistanceMeters { get; set; }
    public double RideDistanceMeters { get; set; }
    public double RideTimeSeconds { get; set; }
    public double WaitTimeSeconds { get; set; }
    public double BurdenScore { get; set; }
}

public class RouteScoreBreakdownDto
{
    public double GeneralizedCostSeconds { get; set; }
    public double TotalDriveSeconds { get; set; }
    public double TotalWalkSeconds { get; set; }
    public double TotalWaitSeconds { get; set; }
    public double DetourPenaltySeconds { get; set; }
    public double FairnessPenaltySeconds { get; set; }
    public double StopComplexityPenaltySeconds { get; set; }
    public double RiskPenaltySeconds { get; set; }
    public double StabilityPenaltySeconds { get; set; }
    public double VenueQualityBonusSeconds { get; set; }
}
    
public class PlannerBenchmarkComparisonDto
{
    public string BaselinePlannerVersion { get; set; } = string.Empty;
    public string ImprovedPlannerVersion { get; set; } = string.Empty;
    public double BaselineGeneralizedCostSeconds { get; set; }
    public double ImprovedGeneralizedCostSeconds { get; set; }
    public double ImprovementPercent { get; set; }
    public int BaselineStopCount { get; set; }
    public int ImprovedStopCount { get; set; }
}

public class CacheDiagnosticsDto
{
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long ExactRouteApiCalls { get; set; }
    public long MatrixApiCalls { get; set; }
}
