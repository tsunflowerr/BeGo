using MediatR;

namespace OptiGo.Application.UseCases;

public record UpdateSessionQueryCommand(Guid SessionId, string QueryText) : IRequest<Unit>;
