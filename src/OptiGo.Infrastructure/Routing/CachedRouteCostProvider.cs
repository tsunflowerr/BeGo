using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
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
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<CachedRouteCostProvider> _logger;
    private readonly ConcurrentDictionary<string, TimedRouteResult> _routeCache = new();
    private readonly ConcurrentDictionary<string, TimedMatrixResult> _matrixCache = new();

    public CachedRouteCostProvider(
        ITravelTimeService travelTimeService,
        ITrafficSnapshotProvider trafficSnapshotProvider,
        IDistributedCache distributedCache,
        ILogger<CachedRouteCostProvider> logger)
    {
        _travelTimeService = travelTimeService;
        _trafficSnapshotProvider = trafficSnapshotProvider;
        _distributedCache = distributedCache;
        _logger = logger;
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
            return cached.Result;

        var distributedKey = $"route:{key}";
        var distributedRoute = await TryGetAsync<RouteResult>(distributedKey, ct);
        if (distributedRoute != null)
        {
            _routeCache[key] = new TimedRouteResult(distributedRoute, DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1)));
            return distributedRoute;
        }

        var result = await _travelTimeService.GetRouteAsync(origin, destination, mode, ct);
        var ttl = effectiveContext.PreferTrafficAware ? TrafficAwareTtl : BaseTtl;
        _routeCache[key] = new TimedRouteResult(result, DateTimeOffset.UtcNow.Add(ttl));
        await TrySetAsync(distributedKey, result, ttl, ct);
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
            return cached.Result;

        var distributedKey = $"matrix:{key}";
        var cachedMatrixDto = await TryGetAsync<TravelMatrixCacheDto>(distributedKey, ct);
        if (cachedMatrixDto != null)
        {
            var cachedMatrix = cachedMatrixDto.ToResult();
            _matrixCache[key] = new TimedMatrixResult(cachedMatrix, DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(1)));
            return cachedMatrix;
        }

        var result = await _travelTimeService.GetTravelMatrixAsync(origins, destinations, mode, ct);
        var ttl = context?.PreferTrafficAware == true ? TrafficAwareTtl : BaseTtl;
        _matrixCache[key] = new TimedMatrixResult(result, DateTimeOffset.UtcNow.Add(ttl));
        await TrySetAsync(distributedKey, TravelMatrixCacheDto.FromResult(result), ttl, ct);
        return result;
    }

    private async Task<T?> TryGetAsync<T>(string key, CancellationToken ct)
    {
        try
        {
            var json = await _distributedCache.GetStringAsync(key, ct);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed route cache get failed for {Key}", key);
            return default;
        }
    }

    private async Task TrySetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await _distributedCache.SetStringAsync(
                key,
                JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Distributed route cache set failed for {Key}", key);
        }
    }

    private sealed record TimedRouteResult(RouteResult Result, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    private sealed record TimedMatrixResult(TravelMatrixResult Result, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    private sealed class TravelMatrixCacheDto
    {
        public double[][] Durations { get; init; } = [];
        public double[][] Distances { get; init; } = [];

        public static TravelMatrixCacheDto FromResult(TravelMatrixResult result) => new()
        {
            Durations = ToJagged(result.Durations),
            Distances = ToJagged(result.Distances)
        };

        public TravelMatrixResult ToResult() => new()
        {
            Durations = ToRectangular(Durations),
            Distances = ToRectangular(Distances)
        };

        private static double[][] ToJagged(double[,] values)
        {
            var rows = values.GetLength(0);
            var cols = values.GetLength(1);
            var result = new double[rows][];
            for (var row = 0; row < rows; row++)
            {
                result[row] = new double[cols];
                for (var col = 0; col < cols; col++)
                {
                    result[row][col] = values[row, col];
                }
            }

            return result;
        }

        private static double[,] ToRectangular(double[][] values)
        {
            var rows = values.Length;
            var cols = rows == 0 ? 0 : values[0].Length;
            var result = new double[rows, cols];
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < values[row].Length; col++)
                {
                    result[row, col] = values[row][col];
                }
            }

            return result;
        }
    }
}
