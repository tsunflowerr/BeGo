using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Infrastructure.ExternalServices.Google;

public class GoogleOptions
{
    public const string SectionName = "Google";
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Sử dụng Google Places API (New) - Tránh lỗi cước phí (Billing) nhờ truyền đúng FieldMask.
/// </summary>
public class GooglePlacesProvider : IPlacesProvider
{
    private readonly HttpClient _httpClient;
    private readonly GoogleOptions _options;
    private readonly ILogger<GooglePlacesProvider> _logger;

    public GooglePlacesProvider(
        HttpClient httpClient,
        IOptions<GoogleOptions> options,
        ILogger<GooglePlacesProvider> logger)
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
        int limit = 20, // Google default max limit per page is 20
        CancellationToken ct = default)
    {
        var url = "https://places.googleapis.com/v1/places:searchNearby";

        // Map category sang Google Place Type
        var primaryType = category.ToLower() switch
        {
            "cafe" => "cafe",
            "coffee" => "coffee_shop",
            "restaurant" => "restaurant",
            "park" => "park",
            _ => "cafe"
        };

        var requestBody = new
        {
            includedTypes = new[] { primaryType },
            maxResultCount = limit > 20 ? 20 : limit,
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude, longitude },
                    radius = radiusMeters
                }
            }
        };

        // Chống lỗi Billing bằng cách gọi chỉ đích danh các field cần thiết
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("X-Goog-FieldMask", "places.id,places.displayName.text,places.primaryType,places.location,places.rating,places.userRatingCount,places.formattedAddress");

        _logger.LogInformation("Calling Google Places API (New) for {Category} at {Lat},{Lng}", primaryType, latitude, longitude);

        var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Google Places API failed: {StatusCode} - {Error}", response.StatusCode, err);
            return Array.Empty<Venue>();
        }

        var result = await response.Content.ReadFromJsonAsync<GooglePlacesResponse>(cancellationToken: ct);
        if (result?.Places == null || result.Places.Count == 0)
        {
            return Array.Empty<Venue>();
        }

        var venues = new List<Venue>();
        foreach (var place in result.Places)
        {
            if (place.Location == null || place.DisplayName == null) continue;

            var venue = new Venue(
                id: place.Id ?? Guid.NewGuid().ToString(),
                name: place.DisplayName.Text ?? "Unnamed",
                category: place.PrimaryType ?? category,
                location: new Coordinate(place.Location.Latitude, place.Location.Longitude),
                rating: place.Rating ?? 3.0,
                reviewCount: place.UserRatingCount ?? 0,
                address: place.FormattedAddress ?? ""
            );

            venues.Add(venue);
        }

        return venues;
    }
}

// Minimal DTOs for Google Places API (New)
internal class GooglePlacesResponse
{
    [JsonPropertyName("places")]
    public List<GooglePlaceItem>? Places { get; set; }
}

internal class GooglePlaceItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public GoogleDisplayName? DisplayName { get; set; }

    [JsonPropertyName("primaryType")]
    public string? PrimaryType { get; set; }

    [JsonPropertyName("location")]
    public GoogleLocation? Location { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("userRatingCount")]
    public int? UserRatingCount { get; set; }

    [JsonPropertyName("formattedAddress")]
    public string? FormattedAddress { get; set; }
}

internal class GoogleDisplayName
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal class GoogleLocation
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}
