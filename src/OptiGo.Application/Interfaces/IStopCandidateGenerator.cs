using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface IStopCandidateGenerator
{
    Task<IReadOnlyList<StopCandidate>> GenerateAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default);
}
