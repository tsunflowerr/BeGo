using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public record AcceptPickupRequestCommand(Guid SessionId, Guid PickupRequestId, Guid DriverId) : IRequest<Unit>;

public class AcceptPickupRequestHandler : IRequestHandler<AcceptPickupRequestCommand, Unit>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;

    public AcceptPickupRequestHandler(
        ISessionRepository sessionRepository,
        IUnitOfWork unitOfWork,
        ISessionNotifier notifier)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
    }

    public async Task<Unit> Handle(AcceptPickupRequestCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);
        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        session.AcceptPickupRequest(request.PickupRequestId, request.DriverId);

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyPickupRequestsUpdatedAsync(session.Id, cancellationToken);

        return Unit.Value;
    }
}
