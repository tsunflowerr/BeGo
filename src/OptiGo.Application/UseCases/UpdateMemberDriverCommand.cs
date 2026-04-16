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

    public UpdateMemberDriverHandler(ISessionRepository sessionRepository, IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(UpdateMemberDriverCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdWithDetailsAsync(request.SessionId, cancellationToken);

        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        session.SetMemberDriver(request.MemberId, request.DriverId);
        PickupPairValidator.ValidateOneToOnePairs(session.Members.ToList());

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
