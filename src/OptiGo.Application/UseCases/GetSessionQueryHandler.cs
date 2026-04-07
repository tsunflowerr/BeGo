using MediatR;
using OptiGo.Application.Interfaces;

namespace OptiGo.Application.UseCases;

/// <summary>
/// Handler xử lý GetSessionQuery.
/// </summary>
public class GetSessionQueryHandler : IRequestHandler<GetSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessionRepository;

    public GetSessionQueryHandler(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<SessionDto?> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        
        if (session == null)
            return null;

        // Xác định host (member đầu tiên tham gia)
        var orderedMembers = session.Members.OrderBy(m => m.JoinedAt).ToList();
        var hostMemberId = orderedMembers.FirstOrDefault()?.Id;

        return new SessionDto
        {
            Id = session.Id,
            HostName = session.HostName,
            Status = session.Status,
            QueryText = session.QueryText,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
            Members = orderedMembers.Select(m => new MemberDto
            {
                Id = m.Id,
                Name = m.Name,
                Latitude = m.Latitude,
                Longitude = m.Longitude,
                TransportMode = m.TransportMode,
                JoinedAt = m.JoinedAt,
                IsHost = m.Id == hostMemberId
            }).ToList(),
            Votes = session.Votes.Select(v => new VoteDto
            {
                MemberId = v.MemberId,
                VenueId = v.VenueId,
                CreatedAt = v.CreatedAt
            }).ToList(),
            NominatedVenueIds = session.NominatedVenueIds.ToList(),
            WinningVenueId = session.WinningVenueId
        };
    }
}
