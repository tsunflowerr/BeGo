using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.UseCases;

public record CreateSessionCommand(
    string HostName,
    double Latitude,
    double Longitude,
    TransportMode TransportMode,
    MemberMobilityRole MobilityRole = MemberMobilityRole.SelfTravel,
    string? AvatarUrl = null,
    string DefaultQuery = "") : MediatR.IRequest<CreateSessionResult>;

public class CreateSessionResult
{
    public Guid SessionId { get; init; }
    public Guid HostMemberId { get; init; }
}

public class CreateSessionHandler : MediatR.IRequestHandler<CreateSessionCommand, CreateSessionResult>
{
    private readonly Application.Interfaces.ISessionRepository _sessionRepository;
    private readonly Application.Interfaces.IUnitOfWork _unitOfWork;
    private readonly Application.Interfaces.ICurrentUser _currentUser;

    public CreateSessionHandler(
        Application.Interfaces.ISessionRepository sessionRepository,
        Application.Interfaces.IUnitOfWork unitOfWork,
        Application.Interfaces.ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<CreateSessionResult> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        var session = new Session(request.HostName);
        if (!string.IsNullOrEmpty(request.DefaultQuery))
        {
            session.SetQueryText(request.DefaultQuery);
        }

        var hostLocation = new Coordinate(request.Latitude, request.Longitude);
        var hostMember = new Member(
            session.Id,
            request.HostName,
            hostLocation,
            request.TransportMode,
            request.MobilityRole,
            request.AvatarUrl,
            _currentUser.Subject,
            _currentUser.Email);
        session.AddMember(hostMember);

        if (hostMember.NeedsPickup())
        {
            session.CreateOrGetPickupRequest(hostMember.Id);
        }

        await _sessionRepository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateSessionResult
        {
            SessionId = session.Id,
            HostMemberId = hostMember.Id
        };
    }
}
