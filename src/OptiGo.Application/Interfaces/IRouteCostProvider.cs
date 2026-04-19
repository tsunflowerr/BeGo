using OptiGo.Application.UseCases;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.Interfaces;

public interface IRouteCostProvider
{
    Task<RouteResult> GetExactRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default);

    Task<TravelMatrixResult> GetEstimatedMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default);

    RouteDiagnosticsSnapshot CaptureSnapshot();
}
