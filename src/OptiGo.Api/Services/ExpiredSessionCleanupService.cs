using OptiGo.Application.Interfaces;

namespace OptiGo.Api.Services;

public class ExpiredSessionCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredSessionCleanupService> _logger;

    public ExpiredSessionCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExpiredSessionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expired session cleanup failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var expired = await repository.GetExpiredAsync(DateTime.UtcNow, 100, ct);
        if (expired.Count == 0)
            return;

        await repository.RemoveRangeAsync(expired, ct);
        await unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Cleaned up {Count} expired sessions.", expired.Count);
    }
}
