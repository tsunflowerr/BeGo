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
    public List<string> PhotoUrls { get; set; } = new();
    public string? AiReviewSummary { get; set; }
    public List<ReviewDto> TopReviews { get; set; } = new();
    public List<MemberRouteDto> MemberRoutes { get; set; } = new();
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
}