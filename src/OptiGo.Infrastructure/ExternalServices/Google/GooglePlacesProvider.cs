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

    // Max photos/reviews để tránh billing cao
    private const int MaxPhotos = 5;
    private const int MaxReviews = 5;
    private const int PhotoMaxWidth = 800;

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

        var primaryType = !string.IsNullOrWhiteSpace(category) ? category.ToLower() : "cafe";

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

    /// <summary>
    /// Lấy thông tin chi tiết của một địa điểm bao gồm ảnh, reviews, và AI summary.
    /// Sử dụng Google Places API (New) GET endpoint.
    /// </summary>
    public async Task<PlaceDetailResult> GetPlaceDetailAsync(string placeId, CancellationToken ct = default)
    {
        var result = new PlaceDetailResult { PlaceId = placeId };

        // Google Places API (New) yêu cầu format: places/{placeId}
        // Nếu placeId đã có prefix "places/" thì dùng luôn, không thì thêm vào
        var resourceName = placeId.StartsWith("places/") ? placeId : $"places/{placeId}";
        var url = $"https://places.googleapis.com/v1/{resourceName}";

        // FieldMask cho Place Details - chỉ lấy các field cần thiết để tối ưu billing
        // photos: Danh sách ảnh của địa điểm
        // reviews: Danh sách reviews từ người dùng
        // generativeSummary: AI-generated summary (nếu có)
        // editorialSummary: Editorial summary fallback
        var fieldMask = "photos,reviews,generativeSummary,editorialSummary";

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Goog-Api-Key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("X-Goog-FieldMask", fieldMask);

        _logger.LogInformation("Calling Google Places Detail API for {PlaceId}", placeId);

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Google Places Detail API failed: {StatusCode} - {Error}", response.StatusCode, err);
                return result;
            }

            var detailResponse = await response.Content.ReadFromJsonAsync<GooglePlaceDetailResponse>(cancellationToken: ct);
            if (detailResponse == null)
            {
                return result;
            }

            // Parse Photos - chuyển photo reference thành URL có thể truy cập
            if (detailResponse.Photos != null && detailResponse.Photos.Count > 0)
            {
                result.PhotoUrls = detailResponse.Photos
                    .Take(MaxPhotos)
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .Select(p => BuildPhotoUrl(p.Name!))
                    .ToList();
            }

            // Parse Reviews
            if (detailResponse.Reviews != null && detailResponse.Reviews.Count > 0)
            {
                result.Reviews = detailResponse.Reviews
                    .Take(MaxReviews)
                    .Select(r => new PlaceReview
                    {
                        AuthorName = r.AuthorAttribution?.DisplayName ?? "Anonymous",
                        Rating = r.Rating ?? 0,
                        Text = r.Text?.Text ?? "",
                        RelativeTime = r.RelativePublishTimeDescription,
                        AuthorPhotoUrl = r.AuthorAttribution?.PhotoUri
                    })
                    .ToList();
            }

            // AI Summary - ưu tiên generativeSummary, fallback editorialSummary
            if (detailResponse.GenerativeSummary?.Overview?.Text != null)
            {
                result.AiReviewSummary = detailResponse.GenerativeSummary.Overview.Text;
            }
            else if (detailResponse.EditorialSummary?.Text != null)
            {
                result.AiReviewSummary = detailResponse.EditorialSummary.Text;
            }

            _logger.LogInformation(
                "Place Detail retrieved: {PlaceId} - {PhotoCount} photos, {ReviewCount} reviews, HasSummary={HasSummary}",
                placeId, result.PhotoUrls.Count, result.Reviews.Count, result.AiReviewSummary != null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get place detail for {PlaceId}", placeId);
        }

        return result;
    }

    /// <summary>
    /// Build URL để lấy ảnh từ Google Places API (New).
    /// Format: https://places.googleapis.com/v1/{photo_name}/media?maxWidthPx={width}&key={apiKey}
    /// </summary>
    private string BuildPhotoUrl(string photoName)
    {
        return $"https://places.googleapis.com/v1/{photoName}/media?maxWidthPx={PhotoMaxWidth}&key={_options.ApiKey}";
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

// DTOs for Google Places Detail API (New)
internal class GooglePlaceDetailResponse
{
    [JsonPropertyName("photos")]
    public List<GooglePhoto>? Photos { get; set; }

    [JsonPropertyName("reviews")]
    public List<GoogleReview>? Reviews { get; set; }

    [JsonPropertyName("generativeSummary")]
    public GoogleGenerativeSummary? GenerativeSummary { get; set; }

    [JsonPropertyName("editorialSummary")]
    public GoogleLocalizedText? EditorialSummary { get; set; }
}

internal class GooglePhoto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("widthPx")]
    public int? WidthPx { get; set; }

    [JsonPropertyName("heightPx")]
    public int? HeightPx { get; set; }

    [JsonPropertyName("authorAttributions")]
    public List<GoogleAuthorAttribution>? AuthorAttributions { get; set; }
}

internal class GoogleReview
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("relativePublishTimeDescription")]
    public string? RelativePublishTimeDescription { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("text")]
    public GoogleLocalizedText? Text { get; set; }

    [JsonPropertyName("originalText")]
    public GoogleLocalizedText? OriginalText { get; set; }

    [JsonPropertyName("authorAttribution")]
    public GoogleAuthorAttribution? AuthorAttribution { get; set; }
}

internal class GoogleAuthorAttribution
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("photoUri")]
    public string? PhotoUri { get; set; }
}

internal class GoogleLocalizedText
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }
}

internal class GoogleGenerativeSummary
{
    [JsonPropertyName("overview")]
    public GoogleLocalizedText? Overview { get; set; }

    [JsonPropertyName("description")]
    public GoogleLocalizedText? Description { get; set; }
}
