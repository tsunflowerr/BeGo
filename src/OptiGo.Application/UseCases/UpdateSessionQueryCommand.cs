using MediatR;

namespace OptiGo.Application.UseCases;

/// <summary>
/// Command để cập nhật query text của Session.
/// </summary>
public record UpdateSessionQueryCommand(Guid SessionId, string QueryText) : IRequest<Unit>;
