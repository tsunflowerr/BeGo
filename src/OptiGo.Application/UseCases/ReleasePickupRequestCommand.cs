using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public record ReleasePickupRequestCommand(Guid SessionId, Guid PickupRequestId) : IRequest<Unit>;

public class ReleasePickupRequestHandler : IRequestHandler<ReleasePickupRequestCommand, Unit>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public ReleasePickupRequestHandler(
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

    public async Task<Unit> Handle(ReleasePickupRequestCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        var pickupRequest = session.PickupRequests.FirstOrDefault(r => r.Id == request.PickupRequestId)
            ?? throw new DomainException("Pickup request not found.");
        var currentMember = SessionAuthorization.RequireCurrentMember(session, _currentUser);
        var hostMemberId = session.Members.OrderBy(m => m.JoinedAt).FirstOrDefault()?.Id;
        if (currentMember.Id != hostMemberId &&
            currentMember.Id != pickupRequest.PassengerId &&
            currentMember.Id != pickupRequest.AcceptedDriverId)
        {
            throw new DomainException("Current user cannot release this pickup request.");
        }

        session.ReleasePickupRequest(request.PickupRequestId);

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyPickupRequestsUpdatedAsync(session.Id, cancellationToken);

        return Unit.Value;
    }
}
