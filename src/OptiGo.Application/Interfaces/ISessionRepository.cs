using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

/// <summary>
/// Port — Lớp Application khai báo contract, lớp Infrastructure triển khai.
/// Tuân thủ Dependency Inversion Principle (SOLID).
/// </summary>
public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// GetByIdWithMembersAsync — Eager load Members & Votes cùng Session.
    /// </summary>
    Task<Session?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
}
