using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Tests.Routing;

internal static class TestRoutingSupport
{
    public static Session CreateSessionWithAcceptedRequests(
        Member driver,
        params Member[] passengers)
    {
        var session = new Session("host");
        session.AddMember(driver);
        foreach (var passenger in passengers)
        {
            session.AddMember(passenger);
        }

        foreach (var passenger in passengers)
        {
            session.CreateOrGetPickupRequest(passenger.Id);
        }

        foreach (var request in session.PickupRequests.ToList())
        {
            session.AcceptPickupRequest(request.Id, driver.Id);
        }

        return session;
    }

    public static Member CreateMember(
        Guid sessionId,
        string name,
        double latitude,
        double longitude,
        TransportMode transportMode,
        MemberMobilityRole mobilityRole) =>
        new(
            sessionId,
            name,
            new Coordinate(latitude, longitude),
            transportMode,
            mobilityRole);
}

internal sealed class FakeRouteCostProvider : IRouteCostProvider
{
    public Task<RouteResult> GetExactRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default)
    {
        var distance = origin.DistanceTo(destination);
        return Task.FromResult(new RouteResult
        {
            DistanceMeters = distance,
            DurationSeconds = distance / GetSpeedMetersPerSecond(mode)
        });
    }

    public Task<TravelMatrixResult> GetEstimatedMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default)
    {
        var durations = new double[origins.Count, destinations.Count];
        var distances = new double[origins.Count, destinations.Count];
        for (var i = 0; i < origins.Count; i++)
        {
            for (var j = 0; j < destinations.Count; j++)
            {
                distances[i, j] = origins[i].DistanceTo(destinations[j]);
                durations[i, j] = distances[i, j] / GetSpeedMetersPerSecond(mode);
            }
        }

        return Task.FromResult(new TravelMatrixResult
        {
            Durations = durations,
            Distances = distances
        });
    }

    private static double GetSpeedMetersPerSecond(TransportMode mode) => mode switch
    {
        TransportMode.Walking => 1.3,
        TransportMode.Cycling => 4.2,
        TransportMode.Motorbike => 8.3,
        TransportMode.Car => 7.5,
        TransportMode.Bus => 6.0,
        _ => 7.5
    };
}

internal sealed class FakeTrafficSnapshotProvider : ITrafficSnapshotProvider
{
    public TrafficSnapshot GetCurrentSnapshot() => new("test-bucket", 1.0, false);
}

internal sealed class FixedStopCandidateGenerator : IStopCandidateGenerator
{
    private readonly IReadOnlyList<StopCandidate> _candidates;

    public FixedStopCandidateGenerator(IReadOnlyList<StopCandidate> candidates)
    {
        _candidates = candidates;
    }

    public Task<IReadOnlyList<StopCandidate>> GenerateAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default) =>
        Task.FromResult(_candidates);
}
