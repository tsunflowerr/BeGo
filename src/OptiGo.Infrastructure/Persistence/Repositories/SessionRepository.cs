using Microsoft.EntityFrameworkCore;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly OptiGoDbContext _db;

    public SessionRepository(OptiGoDbContext db)
    {
        _db = db;
    }

    public async Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sessions.FindAsync(new object[] { id }, ct);
    }

    public async Task<Session?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sessions
            .Include(s => s.Members)
            .Include(s => s.Votes)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task AddAsync(Session session, CancellationToken ct = default)
    {
        await _db.Sessions.AddAsync(session, ct);
    }

    public Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        // EF Core change tracking sẽ tự phát hiện thay đổi khi SaveChanges
        _db.Sessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sessions.AnyAsync(s => s.Id == id, ct);
    }
}
