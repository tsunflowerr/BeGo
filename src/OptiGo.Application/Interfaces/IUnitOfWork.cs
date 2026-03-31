namespace OptiGo.Application.Interfaces;

/// <summary>
/// Unit of Work — đảm bảo tất cả thay đổi trong 1 transaction được commit atomic.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
