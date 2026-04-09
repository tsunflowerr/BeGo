using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;

namespace OptiGo.Application.UseCases;

public record JoinSessionCommand(
    Guid SessionId,
    string MemberName,
    double Latitude,
    double Longitude,
    TransportMode TransportMode) : IRequest<Guid>;

public class JoinSessionHandler : IRequestHandler<JoinSessionCommand, Guid>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;

    public JoinSessionHandler(ISessionRepository sessionRepository, IUnitOfWork unitOfWork, ISessionNotifier notifier)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
    }

    public async Task<Guid> Handle(JoinSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null) throw new Exception("Session not found");

        var coordinate = new Domain.ValueObjects.Coordinate(request.Latitude, request.Longitude);
        var member = new Member(request.SessionId, request.MemberName, coordinate, request.TransportMode);

        session.AddMember(member);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _notifier.NotifyMemberJoinedAsync(
            session.Id,
            member.Id,
            member.Name,
            member.Latitude,
            member.Longitude,
            member.TransportMode,
            member.JoinedAt,
            session.Members.Count == 1,
            session.Members.Count,
            cancellationToken);

        return member.Id;
    }
}
