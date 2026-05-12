using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public record LockDepartureCommand(Guid SessionId) : IRequest<Unit>;

public class LockDepartureHandler : IRequestHandler<LockDepartureCommand, Unit>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public LockDepartureHandler(
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

    public async Task<Unit> Handle(LockDepartureCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        SessionAuthorization.RequireHost(session, _currentUser);

        session.LockDeparture();

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyDepartureLockedAsync(session.Id, cancellationToken);

        return Unit.Value;
    }
}
