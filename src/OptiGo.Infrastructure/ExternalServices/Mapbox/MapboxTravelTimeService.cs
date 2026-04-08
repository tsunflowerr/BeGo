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
/// Matrix API trả về cả duration và distance trong 1 request - tối ưu billing.
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
        // Delegate to GetTravelMatrixAsync and return only durations
        var result = await GetTravelMatrixAsync(origins, destinations, mode, ct);
        return result.Durations;
    }

    /// <summary>
    /// Lấy cả ma trận duration và distance trong 1 API call.
    /// Tối ưu billing so với gọi Directions API riêng cho từng cặp.
    /// </summary>
    public async Task<TravelMatrixResult> GetTravelMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default)
    {
        if (origins.Count == 0 || destinations.Count == 0)
        {
            return new TravelMatrixResult
            {
                Durations = new double[origins.Count, destinations.Count],
                Distances = new double[origins.Count, destinations.Count]
            };
        }

        var profile = MapboxTransportModeMapper.ToMapboxProfile(mode);
        var adjustmentFactor = MapboxTransportModeMapper.GetAdjustmentFactor(mode);

        // Convert List<List<double?>> thành double[,] matrices
        var durations = new double[origins.Count, destinations.Count];
        var distances = new double[origins.Count, destinations.Count];

        var maxOriginsPerBatch = Math.Min(origins.Count, MaxCoordinatesPerRequest - 1);
        for (int originStart = 0; originStart < origins.Count; originStart += maxOriginsPerBatch)
        {
            var originBatch = origins.Skip(originStart).Take(maxOriginsPerBatch).ToList();
            var maxDestinationsPerBatch = MaxCoordinatesPerRequest - originBatch.Count;

            if (maxDestinationsPerBatch <= 0)
            {
                throw new InvalidOperationException("Mapbox batching failed because origin batch consumed all available coordinate slots.");
            }

            for (int destinationStart = 0; destinationStart < destinations.Count; destinationStart += maxDestinationsPerBatch)
            {
                var destinationBatch = destinations.Skip(destinationStart).Take(maxDestinationsPerBatch).ToList();
                var partialResult = await GetTravelMatrixChunkAsync(profile, originBatch, destinationBatch, ct);

                for (int originIndex = 0; originIndex < originBatch.Count; originIndex++)
                {
                    for (int destinationIndex = 0; destinationIndex < destinationBatch.Count; destinationIndex++)
                    {
                        var globalOriginIndex = originStart + originIndex;
                        var globalDestinationIndex = destinationStart + destinationIndex;

                        durations[globalOriginIndex, globalDestinationIndex] =
                            partialResult.Durations[originIndex, destinationIndex] * adjustmentFactor;

                        distances[globalOriginIndex, globalDestinationIndex] =
                            partialResult.Distances[originIndex, destinationIndex];
                    }
                }
            }
        }

        return new TravelMatrixResult
        {
            Durations = durations,
            Distances = distances
        };
    }

    private async Task<TravelMatrixResult> GetTravelMatrixChunkAsync(
        string profile,
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        CancellationToken ct)
    {
        var allCoords = origins.Concat(destinations).ToList();
        var coordinatesStr = string.Join(";",
            allCoords.Select(c => $"{c.Longitude:F6},{c.Latitude:F6}"));

        var sourceIndices = string.Join(";", Enumerable.Range(0, origins.Count));
        var destIndices = string.Join(";", Enumerable.Range(origins.Count, destinations.Count));

        var url = $"{_options.BaseUrl}/{profile}/{coordinatesStr}" +
                  $"?sources={sourceIndices}" +
                  $"&destinations={destIndices}" +
                  $"&annotations=duration,distance" +
                  $"&access_token={_options.ApiKey}";

        _logger.LogInformation(
            "Calling Mapbox Matrix API chunk: profile={Profile}, origins={Origins}, destinations={Destinations}",
            profile, origins.Count, destinations.Count);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MapboxMatrixResponse>(cancellationToken: ct);
        if (result?.Durations is null)
        {
            throw new InvalidOperationException("Mapbox Matrix API returned null durations.");
        }

        var durations = new double[origins.Count, destinations.Count];
        var distances = new double[origins.Count, destinations.Count];

        for (int i = 0; i < origins.Count; i++)
        {
            for (int j = 0; j < destinations.Count; j++)
            {
                if (i >= result.Durations.Count || j >= result.Durations[i].Count)
                {
                    durations[i, j] = 999999;
                    distances[i, j] = 999999;
                    continue;
                }

                durations[i, j] = result.Durations[i][j] ?? 999999.0;

                if (result.Distances != null && i < result.Distances.Count && j < result.Distances[i].Count)
                {
                    distances[i, j] = result.Distances[i][j] ?? 999999;
                }
                else
                {
                    distances[i, j] = origins[i].DistanceTo(destinations[j]);
                }
            }
        }

        return new TravelMatrixResult
        {
            Durations = durations,
            Distances = distances
        };
    }
}

// Mapbox Matrix API Response DTO
internal class MapboxMatrixResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("durations")]
    public List<List<double?>>? Durations { get; set; }

    [JsonPropertyName("distances")]
    public List<List<double?>>? Distances { get; set; }
}
