using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IPlacesProvider
{
    /// <summary>
    /// Tìm kiếm danh sách địa điểm xung quanh tọa độ chỉ định.
    /// Có thể dùng Mapbox, Google Places, hoặc OpenStreetMap Overpass.
    /// </summary>
    /// <param name="latitude">Vĩ độ (Geometric Median)</param>
    /// <param name="longitude">Kinh độ</param>
    /// <param name="category">Loại hình (VD: cafe, restaurant, park)</param>
    /// <param name="radiusMeters">Bán kính quét (mặc định 2000m)</param>
    /// <param name="limit">Chỉ lấy top N kết quả</param>
    Task<IReadOnlyList<Venue>> SearchNearbyAsync(
        double latitude, 
        double longitude, 
        string category, 
        double radiusMeters = 2000,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy thông tin chi tiết của một địa điểm bao gồm ảnh, reviews, và AI summary.
    /// </summary>
    /// <param name="placeId">ID của địa điểm (Google Place ID)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Chi tiết địa điểm bao gồm photos, reviews, AI summary</returns>
    Task<PlaceDetailResult> GetPlaceDetailAsync(string placeId, CancellationToken ct = default);
}

/// <summary>
/// Kết quả chi tiết của một địa điểm từ Places API.
/// </summary>
public class PlaceDetailResult
{
    public string PlaceId { get; set; } = null!;
    public List<string> PhotoUrls { get; set; } = new();
    public string? AiReviewSummary { get; set; }
    public List<PlaceReview> Reviews { get; set; } = new();
}

/// <summary>
/// Thông tin một review từ người dùng.
/// </summary>
public class PlaceReview
{
    public string AuthorName { get; set; } = null!;
    public double Rating { get; set; }
    public string Text { get; set; } = null!;
    public string? RelativeTime { get; set; }
    public string? AuthorPhotoUrl { get; set; }
}
