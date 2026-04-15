using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.Interfaces;

public interface ITravelTimeService
{

    Task<double[,]> GetTravelTimeMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default);

    Task<TravelMatrixResult> GetTravelMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default);
    Task<RouteResult> GetRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        CancellationToken ct = default);
}

public class TravelMatrixResult
{

    public double[,] Durations { get; set; } = new double[0, 0];

    public double[,] Distances { get; set; } = new double[0, 0];
}

public class RouteResult
{
    public double DurationSeconds { get; set; }
    public double DistanceMeters { get; set; }
}
