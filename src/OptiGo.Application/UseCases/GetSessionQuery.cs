using MediatR;
using OptiGo.Domain.Enums;

namespace OptiGo.Application.UseCases;

public record GetSessionQuery(Guid SessionId) : IRequest<SessionDto?>;

public class SessionDto
{
    public Guid Id { get; init; }
    public string HostName { get; init; } = string.Empty;
    public SessionStatus Status { get; init; }
    public string? QueryText { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public List<MemberDto> Members { get; init; } = new();
    public List<VoteDto> Votes { get; init; } = new();
    public List<string> NominatedVenueIds { get; init; } = new();
    public string? WinningVenueId { get; init; }
}

public class MemberDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public TransportMode TransportMode { get; init; }
    public DateTime JoinedAt { get; init; }
    public bool IsHost { get; init; }
}

public class VoteDto
{
    public Guid MemberId { get; init; }
    public string VenueId { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
