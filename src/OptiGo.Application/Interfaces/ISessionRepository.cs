using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Session?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
