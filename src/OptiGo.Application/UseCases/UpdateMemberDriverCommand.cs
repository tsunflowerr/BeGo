using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Exceptions;
using OptiGo.Domain.Services;

namespace OptiGo.Application.UseCases;

public record UpdateMemberDriverCommand(Guid SessionId, Guid MemberId, Guid? DriverId) : IRequest<Unit>;

public class UpdateMemberDriverHandler : IRequestHandler<UpdateMemberDriverCommand, Unit>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionNotifier _notifier;
    private readonly ICurrentUser _currentUser;

    public UpdateMemberDriverHandler(ISessionRepository sessionRepository, IUnitOfWork unitOfWork, ISessionNotifier notifier, ICurrentUser currentUser)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(UpdateMemberDriverCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);

        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        SessionAuthorization.RequireHost(session, _currentUser);

        session.SetMemberDriver(request.MemberId, request.DriverId);

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _notifier.NotifyPickupRequestsUpdatedAsync(session.Id, cancellationToken);

        return Unit.Value;
    }
}
