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
}
