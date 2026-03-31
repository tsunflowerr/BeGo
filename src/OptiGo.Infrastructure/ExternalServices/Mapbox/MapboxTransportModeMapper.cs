using OptiGo.Domain.Enums;

namespace OptiGo.Infrastructure.ExternalServices.Mapbox;

/// <summary>
/// Maps Domain TransportMode → Mapbox Profile string.
/// Khi chuyển sang Google Matrix API sau này, bạn chỉ cần tạo 1 class mới
/// (GoogleTransportModeMapper) mà KHÔNG cần sửa bất kỳ code nào ở Domain/Application.
/// </summary>
public static class MapboxTransportModeMapper
{
    /// <summary>
    /// Mapbox chỉ hỗ trợ 4 profiles: driving-traffic, driving, walking, cycling.
    /// - Motorbike → driving-traffic (giao thông thực tế, gần nhất với xe máy VN)
    /// - Car      → driving-traffic
    /// - Bus      → driving-traffic (Mapbox không có transit, tạm dùng driving-traffic)
    /// - Walking  → walking
    /// - Cycling  → cycling
    /// </summary>
    public static string ToMapboxProfile(TransportMode mode) => mode switch
    {
        TransportMode.Walking  => "walking",
        TransportMode.Cycling  => "cycling",
        TransportMode.Motorbike => "driving",
        TransportMode.Car      => "driving",
        TransportMode.Bus      => "driving",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode")
    };

    /// <summary>
    /// Hệ số nhân điều chỉnh thời gian cho từng phương tiện.
    /// Mapbox trả về time cho ô tô, ta nhân hệ số để ước lượng cho phương tiện khác.
    /// Khi dùng Google Transit API sau này, hệ số này không cần thiết nữa.
    /// </summary>
    public static double GetAdjustmentFactor(TransportMode mode) => mode switch
    {
        TransportMode.Walking   => 1.0,  // walking profile đã chính xác
        TransportMode.Cycling   => 1.0,  // cycling profile đã chính xác
        TransportMode.Motorbike => 0.85, // xe máy nhanh hơn ô tô ~15% trong nội thành VN
        TransportMode.Car       => 1.0,  // driving-traffic đã chính xác cho ô tô
        TransportMode.Bus       => 1.8,  // bus chậm hơn ô tô ~80% (dừng trạm, tuyến vòng)
        _ => 1.0
    };
}
