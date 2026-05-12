using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IChatMessageRepository
{
    Task<IReadOnlyList<ChatMessage>> GetRecentForSessionAsync(Guid sessionId, int take, CancellationToken ct = default);
    Task AddAsync(ChatMessage message, CancellationToken ct = default);
}
