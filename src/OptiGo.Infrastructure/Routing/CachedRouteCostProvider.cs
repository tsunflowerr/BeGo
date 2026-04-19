using System.Collections.Concurrent;
using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.Routing;

public class CachedRouteCostProvider : IRouteCostProvider
{
    private static readonly TimeSpan BaseTtl = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan TrafficAwareTtl = TimeSpan.FromMinutes(5);

    private readonly ITravelTimeService _travelTimeService;
    private readonly ITrafficSnapshotProvider _trafficSnapshotProvider;
    private readonly ConcurrentDictionary<string, TimedRouteResult> _routeCache = new();
    private readonly ConcurrentDictionary<string, TimedMatrixResult> _matrixCache = new();

    private long _cacheHits;
    private long _cacheMisses;
    private long _exactRouteApiCalls;
    private long _matrixApiCalls;

    public CachedRouteCostProvider(
        ITravelTimeService travelTimeService,
        ITrafficSnapshotProvider trafficSnapshotProvider)
    {
        _travelTimeService = travelTimeService;
        _trafficSnapshotProvider = trafficSnapshotProvider;
    }

    public async Task<RouteResult> GetExactRouteAsync(
        Coordinate origin,
        Coordinate destination,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default)
    {
        var snapshot = context is null || string.IsNullOrWhiteSpace(context.TrafficBucketKey)
            ? _trafficSnapshotProvider.GetCurrentSnapshot()
            : new TrafficSnapshot(context.TrafficBucketKey);
        var effectiveContext = context ?? new RouteCostContext(false, snapshot.BucketKey);

        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{origin.Latitude:F5}|{origin.Longitude:F5}|{destination.Latitude:F5}|{destination.Longitude:F5}|{(int)mode}|{effectiveContext.PreferTrafficAware}|{effectiveContext.TrafficBucketKey}");

        if (_routeCache.TryGetValue(key, out var cached) && !cached.IsExpired)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached.Result;
        }

        Interlocked.Increment(ref _cacheMisses);
        Interlocked.Increment(ref _exactRouteApiCalls);
        var result = await _travelTimeService.GetRouteAsync(origin, destination, mode, ct);
        var ttl = effectiveContext.PreferTrafficAware ? TrafficAwareTtl : BaseTtl;
        _routeCache[key] = new TimedRouteResult(result, DateTimeOffset.UtcNow.Add(ttl));
        return result;
    }

    public async Task<TravelMatrixResult> GetEstimatedMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        RouteCostContext? context = null,
        CancellationToken ct = default)
    {
        var snapshot = context is null || string.IsNullOrWhiteSpace(context.TrafficBucketKey)
            ? _trafficSnapshotProvider.GetCurrentSnapshot()
            : new TrafficSnapshot(context.TrafficBucketKey);

        var key = string.Join(
            "|",
            (context?.PreferTrafficAware ?? false).ToString(),
            snapshot.BucketKey,
            (int)mode,
            string.Join(";", origins.Select(coord => $"{coord.Latitude:F4},{coord.Longitude:F4}")),
            string.Join(";", destinations.Select(coord => $"{coord.Latitude:F4},{coord.Longitude:F4}")));

        if (_matrixCache.TryGetValue(key, out var cached) && !cached.IsExpired)
        {
            Interlocked.Increment(ref _cacheHits);
            return cached.Result;
        }

        Interlocked.Increment(ref _cacheMisses);
        Interlocked.Increment(ref _matrixApiCalls);
        var result = await _travelTimeService.GetTravelMatrixAsync(origins, destinations, mode, ct);
        var ttl = context?.PreferTrafficAware == true ? TrafficAwareTtl : BaseTtl;
        _matrixCache[key] = new TimedMatrixResult(result, DateTimeOffset.UtcNow.Add(ttl));
        return result;
    }

    public RouteDiagnosticsSnapshot CaptureSnapshot() =>
        new(
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheMisses),
            Interlocked.Read(ref _exactRouteApiCalls),
            Interlocked.Read(ref _matrixApiCalls));

    private sealed record TimedRouteResult(RouteResult Result, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    private sealed record TimedMatrixResult(TravelMatrixResult Result, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
