using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public record JoinSessionCommand(
    Guid SessionId,
    string MemberName,
    double Latitude,
    double Longitude,
    TransportMode TransportMode,
    MemberMobilityRole MobilityRole = MemberMobilityRole.SelfTravel,
    string? AvatarUrl = null) : IRequest<Guid>;

public record AddTestMemberCommand(
    Guid SessionId,
    string MemberName,
    double Latitude,
    double Longitude,
    TransportMode TransportMode,
    MemberMobilityRole MobilityRole = MemberMobilityRole.SelfTravel,
    string? AvatarUrl = null) : IRequest<Guid>;

public class JoinSessionHandler : IRequestHandler<JoinSessionCommand, Guid>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public JoinSessionHandler(ISessionRepository sessionRepository, IUnitOfWork unitOfWork, ISessionNotifier notifier, ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(JoinSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null) throw new DomainException("Session not found");

        var coordinate = new Domain.ValueObjects.Coordinate(request.Latitude, request.Longitude);
        var member = new Member(
            request.SessionId,
            request.MemberName,
            coordinate,
            request.TransportMode,
            request.MobilityRole,
            request.AvatarUrl,
            _currentUser.Subject,
            _currentUser.Email);

        session.AddMember(member);
        if (member.NeedsPickup())
        {
            session.CreateOrGetPickupRequest(member.Id);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyMemberJoinedAsync(
            session.Id,
            member.Id,
            member.Name,
            member.Latitude,
            member.Longitude,
            member.AvatarUrl,
            member.TransportMode,
            member.MobilityRole,
            member.JoinedAt,
            session.Members.Count == 1,
            session.Members.Count,
            cancellationToken);

        return member.Id;
    }
}

public class AddTestMemberHandler : IRequestHandler<AddTestMemberCommand, Guid>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public AddTestMemberHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier,
        ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(AddTestMemberCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null) throw new DomainException("Session not found");

        SessionAuthorization.RequireHost(session, _currentUser);

        var coordinate = new Domain.ValueObjects.Coordinate(request.Latitude, request.Longitude);
        var member = new Member(
            request.SessionId,
            request.MemberName,
            coordinate,
            request.TransportMode,
            request.MobilityRole,
            request.AvatarUrl,
            $"local-test:{request.SessionId}:{Guid.NewGuid():N}",
            null);

        session.AddMember(member);
        if (member.NeedsPickup())
        {
            session.CreateOrGetPickupRequest(member.Id);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyMemberJoinedAsync(
            session.Id,
            member.Id,
            member.Name,
            member.Latitude,
            member.Longitude,
            member.AvatarUrl,
            member.TransportMode,
            member.MobilityRole,
            member.JoinedAt,
            false,
            session.Members.Count,
            cancellationToken);

        return member.Id;
    }
}
