using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.ExternalServices.Google;

/// <summary>
/// Sử dụng Google Distance Matrix API để đo khoảng cách thực tế giữa các members và top địa điểm.
/// Có khả năng đo thời gian Transit (Bus) cực kỳ chính xác thay vì phải nhân hệ số như Mapbox.
/// Google Distance Matrix API trả về cả duration và distance trong 1 request.
/// </summary>
public class GoogleTravelTimeService : ITravelTimeService
{
    private readonly HttpClient _httpClient;
    private readonly GoogleOptions _options;
    private readonly ILogger<GoogleTravelTimeService> _logger;

    public GoogleTravelTimeService(
        HttpClient httpClient,
        IOptions<GoogleOptions> options,
        ILogger<GoogleTravelTimeService> logger)
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
    /// Google Distance Matrix API tự động trả về cả 2.
    /// </summary>
    public async Task<TravelMatrixResult> GetTravelMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default)
    {
        string travelMode = mode switch
        {
            TransportMode.Walking => "walking",
            TransportMode.Cycling => "bicycling",
            TransportMode.Motorbike => "driving",
            TransportMode.Car => "driving",
            TransportMode.Bus => "transit",
            _ => "driving"
        };

        // Format: lat,lng|lat,lng...
        var originsStr = string.Join("|", origins.Select(c => $"{c.Latitude:F6},{c.Longitude:F6}"));
        var destsStr = string.Join("|", destinations.Select(c => $"{c.Latitude:F6},{c.Longitude:F6}"));

        var url = $"https://maps.googleapis.com/maps/api/distancematrix/json" +
                  $"?origins={originsStr}" +
                  $"&destinations={destsStr}" +
                  $"&mode={travelMode}" +
                  $"&key={_options.ApiKey}";

        _logger.LogInformation("Calling Google Distance Matrix API for mode {Mode}", travelMode);

        var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"Google Distance Matrix failed: {err}");
        }

        var result = await response.Content.ReadFromJsonAsync<GoogleMatrixResponse>(cancellationToken: ct);
        if (result?.Status != "OK" || result.Rows == null)
        {
            throw new Exception($"Google API returned status: {result?.Status}");
        }

        var durations = new double[origins.Count, destinations.Count];
        var distances = new double[origins.Count, destinations.Count];

        for (int i = 0; i < origins.Count; i++)
        {
            if (i >= result.Rows.Count) continue;
            var elements = result.Rows[i].Elements;

            for (int j = 0; j < destinations.Count; j++)
            {
                if (elements == null || j >= elements.Count) continue;

                var element = elements[j];
                if (element.Status == "OK")
                {
                    durations[i, j] = element.Duration?.Value ?? 999999;
                    distances[i, j] = element.Distance?.Value ?? origins[i].DistanceTo(destinations[j]);
                }
                else
                {
                    durations[i, j] = 999999;
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

// Minimal DTOs
internal class GoogleMatrixResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("rows")]
    public List<GoogleMatrixRow>? Rows { get; set; }
}

internal class GoogleMatrixRow
{
    [JsonPropertyName("elements")]
    public List<GoogleMatrixElement>? Elements { get; set; }
}

internal class GoogleMatrixElement
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("duration")]
    public GoogleMatrixValue? Duration { get; set; }

    [JsonPropertyName("distance")]
    public GoogleMatrixValue? Distance { get; set; }
}

internal class GoogleMatrixValue
{
    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
