using MediatR;
using OptiGo.Application.Interfaces;
using System.Text.Json;

namespace OptiGo.Application.UseCases;

public class GetSessionQueryHandler : IRequestHandler<GetSessionQuery, SessionDto?>
{
    private readonly ISessionRepository _sessionRepository;
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public GetSessionQueryHandler(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<SessionDto?> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);

        if (session == null)
            return null;

        var orderedMembers = session.Members.OrderBy(m => m.JoinedAt).ToList();
        var hostMemberId = orderedMembers.FirstOrDefault()?.Id;
        var latestOptimizationResult = Deserialize<FindMeetPointResult>(session.LatestOptimizationSnapshotJson);
        var finalRoutePreview = Deserialize<CandidateResultDto>(session.FinalRouteSnapshotJson);

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
                MobilityRole = m.MobilityRole,
                DriverId = m.DriverId,
                CanOfferPickup = m.CanOfferPickup(),
                AvailableSeatCount = Math.Max(0, m.GetSeatCapacity() - session.GetAcceptedPassengerCount(m.Id)),
                JoinedAt = m.JoinedAt,
                IsHost = m.Id == hostMemberId
            }).ToList(),
            Votes = session.Votes.Select(v => new VoteDto
            {
                MemberId = v.MemberId,
                VenueId = v.VenueId,
                CreatedAt = v.CreatedAt
            }).ToList(),
            PickupRequests = session.PickupRequests
                .OrderBy(r => r.CreatedAt)
                .Select(r => new PickupRequestDto
                {
                    RequestId = r.Id,
                    PassengerId = r.PassengerId,
                    PassengerName = orderedMembers.FirstOrDefault(m => m.Id == r.PassengerId)?.Name ?? "Unknown",
                    Status = r.Status,
                    AcceptedDriverId = r.AcceptedDriverId,
                    AcceptedDriverName = orderedMembers.FirstOrDefault(m => m.Id == r.AcceptedDriverId)?.Name,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt
                })
                .ToList(),
            NominatedVenueIds = session.NominatedVenueIds.ToList(),
            WinningVenueId = session.WinningVenueId,
            LatestOptimizationResult = latestOptimizationResult,
            FinalRoutePreview = finalRoutePreview,
            DepartureLockedAt = session.DepartureLockedAt
        };
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, SnapshotJsonOptions);
    }
}
