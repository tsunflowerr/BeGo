using MediatR;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

public class UpdateSessionQueryHandler : IRequestHandler<UpdateSessionQueryCommand, Unit>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateSessionQueryHandler(ISessionRepository sessionRepository, IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(UpdateSessionQueryCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        if (session == null)
            throw new DomainException($"Session {request.SessionId} not found.");

        if (session.Status != Domain.Enums.SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot update query after computation has started.");

        session.SetQueryText(request.QueryText);

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
