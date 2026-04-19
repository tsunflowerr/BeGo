using Microsoft.Extensions.Logging;
using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;

namespace OptiGo.Infrastructure.Routing;

public class LoggingRouteBenchmarkRecorder : IRouteBenchmarkRecorder
{
    private readonly ILogger<LoggingRouteBenchmarkRecorder> _logger;

    public LoggingRouteBenchmarkRecorder(ILogger<LoggingRouteBenchmarkRecorder> logger)
    {
        _logger = logger;
    }

    public Task RecordComparisonAsync(
        Guid sessionId,
        CandidateResultDto improvedCandidate,
        CandidateResultDto baselineCandidate,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Routing benchmark for Session {SessionId}, Venue {VenueId}: improved={ImprovedCost}s, baseline={BaselineCost}s, improvement={Improvement:F2}%",
            sessionId,
            improvedCandidate.VenueId,
            improvedCandidate.ScoreBreakdown.GeneralizedCostSeconds,
            baselineCandidate.ScoreBreakdown.GeneralizedCostSeconds,
            improvedCandidate.BenchmarkComparison?.ImprovementPercent ?? 0);

        return Task.CompletedTask;
    }
}
