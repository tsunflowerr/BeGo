using OptiGo.Domain.Entities;

namespace OptiGo.Application.UseCases;

public record CreateSessionCommand(string HostName, string DefaultQuery = "") : MediatR.IRequest<Guid>;

public class CreateSessionHandler : MediatR.IRequestHandler<CreateSessionCommand, Guid>
{
    private readonly Application.Interfaces.ISessionRepository _sessionRepository;
    private readonly Application.Interfaces.IUnitOfWork _unitOfWork;

    public CreateSessionHandler(Application.Interfaces.ISessionRepository sessionRepository, Application.Interfaces.IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateSessionCommand request, CancellationToken cancellationToken)
    {
        var session = new Session(request.HostName);
        if (!string.IsNullOrEmpty(request.DefaultQuery))
        {
            session.SetQueryText(request.DefaultQuery);
        }

        await _sessionRepository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return session.Id;
    }
}
