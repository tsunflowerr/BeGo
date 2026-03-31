using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Application.Interfaces;

/// <summary>
/// Port cho Travel Time Matrix service.
/// Hiện tại Mapbox triển khai, sau này có thể swap sang Google Matrix API
/// mà không cần thay đổi bất kỳ dòng code nào ở Application/Domain layer.
/// </summary>
public interface ITravelTimeService
{
    /// <summary>
    /// Tính ma trận thời gian di chuyển (tính bằng giây).
    /// Mỗi phần tử [i,j] = thời gian từ origins[i] đến destinations[j].
    /// </summary>
    /// <param name="origins">Danh sách tọa độ gốc (vị trí người dùng)</param>
    /// <param name="destinations">Danh sách tọa độ đích (vị trí venue)</param>
    /// <param name="mode">Phương tiện di chuyển</param>
    /// <returns>Ma trận 2D [origins x destinations], giá trị = giây</returns>
    Task<double[,]> GetTravelTimeMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default);
}
