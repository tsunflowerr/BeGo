using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface IRouteBenchmarkRecorder
{
    Task RecordComparisonAsync(
        Guid sessionId,
        CandidateResultDto improvedCandidate,
        CandidateResultDto baselineCandidate,
        CancellationToken ct = default);
}
