using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface IDriverRouteOptimizer
{
    Task<DriverOptimizationResult> OptimizeAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default);
}
