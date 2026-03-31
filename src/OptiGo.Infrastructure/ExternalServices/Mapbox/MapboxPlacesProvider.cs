using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.ExternalServices.Mapbox;

public class MapboxPlacesProvider : IPlacesProvider
{
    private readonly HttpClient _httpClient;
    private readonly MapboxOptions _options;
    private readonly ILogger<MapboxPlacesProvider> _logger;

    public MapboxPlacesProvider(
        HttpClient httpClient,
        IOptions<MapboxOptions> options,
        ILogger<MapboxPlacesProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Venue>> SearchNearbyAsync(
        double latitude, 
        double longitude, 
        string category, 
        double radiusMeters = 2000,
        int limit = 50,
        CancellationToken ct = default)
    {
        // Category API của Mapbox hỗ trợ proximity để tìm xung quanh tọa độ
        // Format API: /search/searchbox/v1/category/{category_name}?proximity={lng},{lat}
        
        // Chuyển "cafe" hoặc "restaurant" thành dạng query của Mapbox (có thể cần tuning sau)
        string queryCategory = MapToMapboxCategory(category);
        
        var url = $"https://api.mapbox.com/search/searchbox/v1/category/{queryCategory}" +
                  $"?access_token={_options.ApiKey}" +
                  $"&proximity={longitude:F6},{latitude:F6}" +
                  $"&limit={limit}";

        _logger.LogInformation("Calling Mapbox Search API: {Category} radius {Lat},{Lng}", queryCategory, latitude, longitude);

        var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Mapbox Places API failed: {StatusCode} - {Error}", response.StatusCode, err);
            return Array.Empty<Venue>();
        }

        var result = await response.Content.ReadFromJsonAsync<MapboxSearchResponse>(cancellationToken: ct);
        if (result?.Features == null || result.Features.Count == 0)
        {
            return Array.Empty<Venue>();
        }

        var venues = new List<Venue>();
        foreach (var feature in result.Features)
        {
            if (feature.Properties == null) continue;
            
            // Mapbox category POIs return coordinates as [lng, lat]
            var coords = feature.Geometry?.Coordinates;
            if (coords == null || coords.Count < 2) continue;

            double lng = coords[0];
            double lat = coords[1];
            
            // Generate a coordinate using Domain VO to validate boundaries
            var coordinate = new Coordinate(lat, lng);

            // Xử lý address và mock một score do Mapbox không trả về rating mặc định
            var address = feature.Properties.Address ?? feature.Properties.FullAddress ?? "Unknown Address";
            
            // Hiện tại dùng Random mock Rating (Lý do: Mapbox API v1 category không support Rating.
            // Để khắc phục triệt để điểm mù này, Google Places là bắt buộc ở Phase sau).
            var mockRating = Math.Round(3.5 + Random.Shared.NextDouble() * 1.5, 1); // 3.5 -> 5.0
            var mockReviews = Random.Shared.Next(10, 500);

            var venue = new Venue(
                id: feature.Properties.MapboxId ?? Guid.NewGuid().ToString(),
                name: feature.Properties.Name ?? "Unnamed",
                category: category,
                location: coordinate,
                rating: mockRating,
                reviewCount: mockReviews,
                address: address
            );

            venues.Add(venue);
        }

        return venues;
    }

    private string MapToMapboxCategory(string category)
    {
        // Simple mapper. Mapbox requires specific POI categories.
        return category.ToLowerInvariant() switch
        {
            "cafe" => "cafe",
            "coffee" => "coffee",
            "restaurant" => "restaurant",
            "food" => "food",
            "bar" => "bar",
            _ => category
        };
    }
}

// Mapbox Search Response DTOs
internal class MapboxSearchResponse
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("features")]
    public List<MapboxFeature>? Features { get; set; }
}

internal class MapboxFeature
{
    [JsonPropertyName("geometry")]
    public MapboxGeometry? Geometry { get; set; }

    [JsonPropertyName("properties")]
    public MapboxProperties? Properties { get; set; }
}

internal class MapboxGeometry
{
    [JsonPropertyName("coordinates")]
    public List<double>? Coordinates { get; set; }
}

internal class MapboxProperties
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("mapbox_id")]
    public string? MapboxId { get; set; }
    
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    
    [JsonPropertyName("full_address")]
    public string? FullAddress { get; set; }
}
