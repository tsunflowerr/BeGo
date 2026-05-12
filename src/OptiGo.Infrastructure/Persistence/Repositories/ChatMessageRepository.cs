using Microsoft.EntityFrameworkCore;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Repositories;

public class ChatMessageRepository : IChatMessageRepository
{
    private readonly OptiGoDbContext _db;

    public ChatMessageRepository(OptiGoDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentForSessionAsync(Guid sessionId, int take, CancellationToken ct = default)
    {
        var boundedTake = Math.Clamp(take, 1, 100);
        return await _db.ChatMessages
            .Where(message => message.SessionId == sessionId)
            .OrderByDescending(message => message.CreatedAt)
            .Take(boundedTake)
            .OrderBy(message => message.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(ChatMessage message, CancellationToken ct = default)
    {
        await _db.ChatMessages.AddAsync(message, ct);
    }
}
