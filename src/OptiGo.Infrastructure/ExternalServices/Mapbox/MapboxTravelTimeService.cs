using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.ExternalServices.Mapbox;

public class MapboxOptions
{
    public const string SectionName = "Mapbox";
    public string BaseUrl { get; set; } = "https://api.mapbox.com/directions-matrix/v1/mapbox";
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Adapter triển khai ITravelTimeService bằng Mapbox Matrix API.
/// Khi muốn chuyển sang Google, tạo 1 class mới implement ITravelTimeService
/// rồi swap DI registration — Application/Domain code không cần thay đổi gì.
/// </summary>
public class MapboxTravelTimeService : ITravelTimeService
{
    private readonly HttpClient _httpClient;
    private readonly MapboxOptions _options;
    private readonly ILogger<MapboxTravelTimeService> _logger;

    // Mapbox Matrix API giới hạn 25 tọa độ mỗi request
    private const int MaxCoordinatesPerRequest = 25;

    public MapboxTravelTimeService(
        HttpClient httpClient,
        IOptions<MapboxOptions> options,
        ILogger<MapboxTravelTimeService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<double[,]> GetTravelTimeMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default)
    {
        var profile = MapboxTransportModeMapper.ToMapboxProfile(mode);
        var adjustmentFactor = MapboxTransportModeMapper.GetAdjustmentFactor(mode);

        // Format: lng1,lat1;lng2,lat2;...
        // Mapbox expects coordinates in lon,lat order (not lat,lon!)
        var allCoords = origins.Concat(destinations).ToList();

        if (allCoords.Count > MaxCoordinatesPerRequest)
        {
            _logger.LogWarning(
                "Mapbox Matrix API limit: {Count} coordinates exceeds max {Max}. Truncating.",
                allCoords.Count, MaxCoordinatesPerRequest);
            // Trong production nên chunk requests, tạm thời truncate
            allCoords = allCoords.Take(MaxCoordinatesPerRequest).ToList();
        }

        var coordinatesStr = string.Join(";",
            allCoords.Select(c => $"{c.Longitude:F6},{c.Latitude:F6}"));

        // sources = indices của origins, destinations = indices của destinations
        var sourceIndices = string.Join(";", Enumerable.Range(0, origins.Count));
        var destIndices = string.Join(";",
            Enumerable.Range(origins.Count, destinations.Count));

        var url = $"{_options.BaseUrl}/{profile}/{coordinatesStr}" +
                  $"?sources={sourceIndices}" +
                  $"&destinations={destIndices}" +
                  $"&annotations=duration" +
                  $"&access_token={_options.ApiKey}";

        _logger.LogInformation(
            "Calling Mapbox Matrix API: profile={Profile}, origins={Origins}, destinations={Destinations}",
            profile, origins.Count, destinations.Count);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MapboxMatrixResponse>(
            cancellationToken: ct);

        if (result?.Durations is null)
        {
            throw new InvalidOperationException("Mapbox Matrix API returned null durations.");
        }

        // Convert List<List<double?>> thành double[,] matrix
        var matrix = new double[origins.Count, destinations.Count];
        for (int i = 0; i < origins.Count; i++)
        {
            for (int j = 0; j < destinations.Count; j++)
            {
                var duration = result.Durations[i][j];
                // null = không tìm được route, set giá trị penalty cao
                matrix[i, j] = (duration ?? double.MaxValue) * adjustmentFactor;
            }
        }

        return matrix;
    }
}

// Mapbox Matrix API Response DTO
internal class MapboxMatrixResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("durations")]
    public List<List<double?>>? Durations { get; set; }
}
