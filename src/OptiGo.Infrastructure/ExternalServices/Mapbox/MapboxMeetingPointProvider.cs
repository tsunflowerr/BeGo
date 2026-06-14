using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.ExternalServices.Mapbox;

public class MapboxMeetingPointProvider : IMeetingPointProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static readonly MeetingPointQuery[] Queries =
    [
        new("convenience store", "convenience_store", 0.95),
        new("cafe", "cafe", 0.85),
        new("gas station", "gas_station", 0.9),
        new("parking", "parking", 0.82),
        new("bus station", "bus_station", 0.8),
        new("shopping mall", "shopping_mall", 0.78)
    ];

    private readonly HttpClient _httpClient;
    private readonly MapboxOptions _options;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MapboxMeetingPointProvider> _logger;

    public MapboxMeetingPointProvider(
        HttpClient httpClient,
        IOptions<MapboxOptions> options,
        IDistributedCache cache,
        ILogger<MapboxMeetingPointProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MeetingPointCandidate>> SearchPickupPointsAsync(
        Coordinate passengerLocation,
        double radiusMeters,
        int limit = 16,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return [];

        var cacheKey = BuildCacheKey(passengerLocation, radiusMeters, limit);
        var cached = await TryGetCachedAsync(cacheKey, ct);
        if (cached != null)
            return cached;

        var unique = new Dictionary<string, MeetingPointCandidate>(StringComparer.OrdinalIgnoreCase);
        var perQueryLimit = Math.Max(2, Math.Min(5, (int)Math.Ceiling(limit / (double)Queries.Length) + 1));

        foreach (var query in Queries)
        {
            try
            {
                var response = await SearchAsync(passengerLocation, radiusMeters, query.Text, perQueryLimit, ct);
                foreach (var feature in response.Features ?? [])
                {
                    var candidate = ToCandidate(feature, query, passengerLocation, radiusMeters);
                    if (candidate == null)
                        continue;

                    unique[candidate.Id] = candidate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Mapbox meeting point search failed for query {Query} around {Lat},{Lng}",
                    query.Text,
                    passengerLocation.Latitude,
                    passengerLocation.Longitude);
            }
        }

        var candidates = unique.Values
            .OrderByDescending(candidate => candidate.PickupFriendlyScore)
            .ThenBy(candidate => passengerLocation.DistanceTo(candidate.Location))
            .Take(limit)
            .ToList();

        await TrySetCachedAsync(cacheKey, candidates, ct);
        return candidates;
    }

    private async Task<IReadOnlyList<MeetingPointCandidate>?> TryGetCachedAsync(
        string cacheKey,
        CancellationToken ct)
    {
        try
        {
            var json = await _cache.GetStringAsync(cacheKey, ct);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<List<MeetingPointCandidate>>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mapbox meeting point cache get failed for {CacheKey}", cacheKey);
            return null;
        }
    }

    private async Task TrySetCachedAsync(
        string cacheKey,
        IReadOnlyList<MeetingPointCandidate> candidates,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(candidates),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mapbox meeting point cache set failed for {CacheKey}", cacheKey);
        }
    }

    private static string BuildCacheKey(Coordinate passengerLocation, double radiusMeters, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"mapbox:pickup-poi:v2:{passengerLocation.Latitude:F4}:{passengerLocation.Longitude:F4}:{radiusMeters:F0}:{limit}:{Queries.Length}");

    private async Task<MapboxSearchBoxResponse> SearchAsync(
        Coordinate passengerLocation,
        double radiusMeters,
        string query,
        int limit,
        CancellationToken ct)
    {
        var bbox = BuildBoundingBox(passengerLocation, radiusMeters);
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{_options.SearchBoxBaseUrl.TrimEnd('/')}/forward" +
            $"?q={Uri.EscapeDataString(query)}" +
            $"&proximity={passengerLocation.Longitude:F6},{passengerLocation.Latitude:F6}" +
            $"&bbox={bbox}" +
            $"&limit={limit}" +
            $"&types=poi" +
            $"&language=vi,en" +
            $"&access_token={_options.ApiKey}");

        var result = await _httpClient.GetFromJsonAsync<MapboxSearchBoxResponse>(url, ct);
        return result ?? new MapboxSearchBoxResponse();
    }

    private static MeetingPointCandidate? ToCandidate(
        MapboxSearchFeature feature,
        MeetingPointQuery query,
        Coordinate passengerLocation,
        double radiusMeters)
    {
        var coordinate = ResolveCoordinate(feature);
        if (coordinate == null)
            return null;

        var location = coordinate.Value;
        var distanceMeters = passengerLocation.DistanceTo(location);
        if (distanceMeters > radiusMeters)
            return null;

        var name = feature.Properties?.Name ?? feature.Properties?.MapboxId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = feature.Properties?.MapboxId ??
                 $"{query.Category}:{Math.Round(location.Latitude, 5):F5}:{Math.Round(location.Longitude, 5):F5}";
        var address = feature.Properties?.FullAddress ??
                      feature.Properties?.PlaceFormatted ??
                      feature.Properties?.Address;
        var distancePenalty = Math.Min(0.35, distanceMeters / Math.Max(1, radiusMeters) * 0.35);
        var namedBonus = name.Length >= 4 ? 0.08 : 0;

        return new MeetingPointCandidate
        {
            Id = id,
            Name = name,
            Category = feature.Properties?.PoiCategory?.FirstOrDefault() ?? query.Category,
            Location = location,
            Address = address,
            PickupFriendlyScore = Math.Clamp(query.BaseScore + namedBonus - distancePenalty, 0, 1)
        };
    }

    private static Coordinate? ResolveCoordinate(MapboxSearchFeature feature)
    {
        if (feature.Properties?.Coordinates != null)
        {
            return new Coordinate(
                feature.Properties.Coordinates.Latitude,
                feature.Properties.Coordinates.Longitude);
        }

        var geometryCoordinates = feature.Geometry?.Coordinates;
        if (geometryCoordinates is { Count: >= 2 })
        {
            return new Coordinate(geometryCoordinates[1], geometryCoordinates[0]);
        }

        return null;
    }

    private static string BuildBoundingBox(Coordinate center, double radiusMeters)
    {
        const double metersPerDegreeLatitude = 111_320;
        var latDelta = radiusMeters / metersPerDegreeLatitude;
        var lngScale = Math.Cos(center.Latitude * Math.PI / 180.0);
        var lngDelta = radiusMeters / Math.Max(1, metersPerDegreeLatitude * Math.Abs(lngScale));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{center.Longitude - lngDelta:F6},{center.Latitude - latDelta:F6},{center.Longitude + lngDelta:F6},{center.Latitude + latDelta:F6}");
    }

    private sealed record MeetingPointQuery(string Text, string Category, double BaseScore);
}

internal class MapboxSearchBoxResponse
{
    [JsonPropertyName("features")]
    public List<MapboxSearchFeature>? Features { get; set; }
}

internal class MapboxSearchFeature
{
    [JsonPropertyName("properties")]
    public MapboxSearchProperties? Properties { get; set; }

    [JsonPropertyName("geometry")]
    public MapboxSearchGeometry? Geometry { get; set; }
}

internal class MapboxSearchProperties
{
    [JsonPropertyName("mapbox_id")]
    public string? MapboxId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("full_address")]
    public string? FullAddress { get; set; }

    [JsonPropertyName("place_formatted")]
    public string? PlaceFormatted { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("poi_category")]
    public List<string>? PoiCategory { get; set; }

    [JsonPropertyName("coordinates")]
    public MapboxSearchCoordinates? Coordinates { get; set; }
}

internal class MapboxSearchCoordinates
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}

internal class MapboxSearchGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double>? Coordinates { get; set; }
}
