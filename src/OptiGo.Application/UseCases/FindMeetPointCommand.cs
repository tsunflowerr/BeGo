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
    
    // Scoring info
    public double TotalTimeSeconds { get; set; }
    public double FinalScore { get; set; }
}
