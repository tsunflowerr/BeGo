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

    private const int MaxNearbyResultsPerRequest = 20;
    private const double DefaultInitialSearchRadiusMeters = 500;
    private const double MaxSearchRadiusMeters = 5000;
    // Max photos/reviews để tránh billing cao
    private const int MaxPhotos = 5;
    private const int MaxReviews = 20;
    private const int PhotoMaxWidth = 800;
    private static readonly double[] SatelliteBearings =
    [
        0, 90, 180, 270,
        45, 135, 225, 315
    ];

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
        double radiusMeters = DefaultInitialSearchRadiusMeters,
        int limit = 50, 
        CancellationToken ct = default)
    {
        var desiredCount = Math.Clamp(limit, 1, 50);
        var primaryType = !string.IsNullOrWhiteSpace(category) ? category.ToLowerInvariant() : "cafe";
        var origin = new Coordinate(latitude, longitude);
        var initialRadius = Math.Clamp(radiusMeters, 50, MaxSearchRadiusMeters);
        var uniqueVenues = new Dictionary<string, Venue>(StringComparer.Ordinal);
        var searchCalls = 0;
        var lastSearchedRadius = initialRadius;

        // Phase 1: mở rộng bán kính đồng tâm từ điểm median.
        // Nearby Search chỉ trả tối đa 20 kết quả/request, nên khi chạm 20 unique từ cùng
        // một tâm thì cần chuyển sang phase 2 để tránh gọi lặp vô ích.
        for (var currentRadius = initialRadius;
             currentRadius <= MaxSearchRadiusMeters && uniqueVenues.Count < Math.Min(desiredCount, MaxNearbyResultsPerRequest);
             currentRadius = Math.Min(currentRadius * 2, MaxSearchRadiusMeters))
        {
            var venues = await SearchNearbyCircleAsync(
                latitude,
                longitude,
                primaryType,
                currentRadius,
                Math.Min(desiredCount, MaxNearbyResultsPerRequest),
                ct);

            searchCalls++;
            lastSearchedRadius = currentRadius;
            MergeVenues(uniqueVenues, venues);

            _logger.LogInformation(
                "Google Nearby concentric search: radius={Radius}m returned {Count}, unique={Unique}/{Target}",
                currentRadius, venues.Count, uniqueVenues.Count, desiredCount);

            if (venues.Count < MaxNearbyResultsPerRequest)
            {
                // Khu vực còn thưa, cứ tiếp tục mở rộng.
                if (currentRadius >= MaxSearchRadiusMeters)
                {
                    break;
                }

                continue;
            }

            if (uniqueVenues.Count >= Math.Min(desiredCount, MaxNearbyResultsPerRequest) ||
                currentRadius >= MaxSearchRadiusMeters)
            {
                break;
            }
        }

        // Phase 2: Nearby Search không thể trả >20 kết quả từ cùng 1 tâm.
        // Khi cần >20 venue, quét thêm một số tâm phụ xung quanh median và khử trùng lặp.
        if (uniqueVenues.Count < desiredCount)
        {
            var ringOffsetMeters = Math.Max(initialRadius * 1.25, 650);
            var satelliteRadius = Math.Max(initialRadius, Math.Min(lastSearchedRadius, 1200));

            while (uniqueVenues.Count < desiredCount && ringOffsetMeters <= MaxSearchRadiusMeters * 1.5)
            {
                var uniqueBeforeRing = uniqueVenues.Count;

                foreach (var bearing in SatelliteBearings)
                {
                    var satelliteCenter = OffsetCoordinate(origin, ringOffsetMeters, bearing);
                    var venues = await SearchNearbyCircleAsync(
                        satelliteCenter.Latitude,
                        satelliteCenter.Longitude,
                        primaryType,
                        satelliteRadius,
                        MaxNearbyResultsPerRequest,
                        ct);

                    searchCalls++;
                    MergeVenues(uniqueVenues, venues);

                    _logger.LogInformation(
                        "Google Nearby satellite search: bearing={Bearing}, offset={Offset}m, radius={Radius}m returned {Count}, unique={Unique}/{Target}",
                        bearing, ringOffsetMeters, satelliteRadius, venues.Count, uniqueVenues.Count, desiredCount);

                    if (uniqueVenues.Count >= desiredCount)
                    {
                        break;
                    }
                }

                if (uniqueVenues.Count == uniqueBeforeRing)
                {
                    break;
                }

                ringOffsetMeters = Math.Min(ringOffsetMeters * 1.6, MaxSearchRadiusMeters * 1.5);
                satelliteRadius = Math.Min(satelliteRadius * 1.25, 1500);
            }
        }

        var orderedVenues = uniqueVenues.Values
            .OrderBy(v => origin.DistanceTo(v.GetLocation()))
            .ThenByDescending(v => v.Rating)
            .ThenByDescending(v => v.ReviewCount)
            .Take(desiredCount)
            .ToList();

        _logger.LogInformation(
            "Google Nearby search completed with {UniqueCount} unique venues after {SearchCalls} paid requests",
            orderedVenues.Count, searchCalls);

        return orderedVenues;
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
        var url = $"https://places.googleapis.com/v1/{resourceName}?languageCode=vi";

        // FieldMask cho Place Details - chỉ lấy các field cần thiết để tối ưu billing
        // photos: Danh sách ảnh của địa điểm
        // reviews: Danh sách reviews từ người dùng
        // generativeSummary: AI-generated summary (nếu có)
        // editorialSummary: Editorial summary fallback
        var fieldMask = "photos,reviews,generativeSummary,editorialSummary";

        _logger.LogInformation("Calling Google Places Detail API for {PlaceId}", placeId);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", fieldMask);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Google Places Detail API failed: {StatusCode} - {Error}", response.StatusCode, err);
                return result;
            }

            var rawJson = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Raw Place Detail JSON: {Json}", rawJson);

            if (string.IsNullOrWhiteSpace(rawJson)) return result;

            var detailResponse = JsonSerializer.Deserialize<GooglePlaceDetailResponse>(rawJson);
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

    private async Task<List<Venue>> SearchNearbyCircleAsync(
        double latitude,
        double longitude,
        string primaryType,
        double radiusMeters,
        int resultCount,
        CancellationToken ct)
    {
        const string url = "https://places.googleapis.com/v1/places:searchNearby";

        var requestBody = new
        {
            includedTypes = new[] { primaryType },
            maxResultCount = Math.Clamp(resultCount, 1, MaxNearbyResultsPerRequest),
            languageCode = "vi",
            rankPreference = "DISTANCE",
            locationRestriction = new
            {
                circle = new
                {
                    center = new { latitude, longitude },
                    radius = radiusMeters
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("X-Goog-Api-Key", _options.ApiKey);
        request.Headers.Add(
            "X-Goog-FieldMask",
            "places.id,places.displayName.text,places.primaryType,places.location,places.rating,places.userRatingCount,places.formattedAddress");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Google Places API failed for type {Category} at {Lat},{Lng}, radius={Radius}: {StatusCode} - {Error}",
                primaryType, latitude, longitude, radiusMeters, response.StatusCode, err);
            return [];
        }

        var result = await response.Content.ReadFromJsonAsync<GooglePlacesResponse>(cancellationToken: ct);
        if (result?.Places == null || result.Places.Count == 0)
        {
            return [];
        }

        var venues = new List<Venue>(result.Places.Count);
        foreach (var place in result.Places)
        {
            if (place.Location == null || place.DisplayName == null || string.IsNullOrWhiteSpace(place.Id))
            {
                continue;
            }

            venues.Add(new Venue(
                id: place.Id,
                name: place.DisplayName.Text ?? "Unnamed",
                category: place.PrimaryType ?? primaryType,
                location: new Coordinate(place.Location.Latitude, place.Location.Longitude),
                rating: place.Rating ?? 3.0,
                reviewCount: place.UserRatingCount ?? 0,
                address: place.FormattedAddress ?? string.Empty
            ));
        }

        return venues;
    }

    private static void MergeVenues(IDictionary<string, Venue> target, IEnumerable<Venue> venues)
    {
        foreach (var venue in venues)
        {
            target[venue.Id] = venue;
        }
    }

    private static Coordinate OffsetCoordinate(Coordinate origin, double distanceMeters, double bearingDegrees)
    {
        const double earthRadiusMeters = 6_371_000;
        var angularDistance = distanceMeters / earthRadiusMeters;
        var bearingRadians = DegreesToRadians(bearingDegrees);
        var latitudeRadians = DegreesToRadians(origin.Latitude);
        var longitudeRadians = DegreesToRadians(origin.Longitude);

        var newLatitude = Math.Asin(
            Math.Sin(latitudeRadians) * Math.Cos(angularDistance) +
            Math.Cos(latitudeRadians) * Math.Sin(angularDistance) * Math.Cos(bearingRadians));

        var newLongitude = longitudeRadians + Math.Atan2(
            Math.Sin(bearingRadians) * Math.Sin(angularDistance) * Math.Cos(latitudeRadians),
            Math.Cos(angularDistance) - Math.Sin(latitudeRadians) * Math.Sin(newLatitude));

        return new Coordinate(RadiansToDegrees(newLatitude), RadiansToDegrees(newLongitude));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
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
