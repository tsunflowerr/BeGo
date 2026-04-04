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

    /// <summary>
    /// Tính ma trận thời gian và khoảng cách di chuyển.
    /// Tối ưu hơn GetTravelTimeMatrixAsync vì trả về cả distance trong 1 API call.
    /// </summary>
    /// <param name="origins">Danh sách tọa độ gốc (vị trí người dùng)</param>
    /// <param name="destinations">Danh sách tọa độ đích (vị trí venue)</param>
    /// <param name="mode">Phương tiện di chuyển</param>
    /// <returns>Kết quả chứa cả ma trận duration (giây) và distance (mét)</returns>
    Task<TravelMatrixResult> GetTravelMatrixAsync(
        IReadOnlyList<Coordinate> origins,
        IReadOnlyList<Coordinate> destinations,
        TransportMode mode,
        CancellationToken ct = default);
}

/// <summary>
/// Kết quả ma trận travel time và distance.
/// </summary>
public class TravelMatrixResult
{
    /// <summary>
    /// Ma trận thời gian di chuyển [origins × destinations], giá trị = giây.
    /// </summary>
    public double[,] Durations { get; set; } = new double[0, 0];

    /// <summary>
    /// Ma trận khoảng cách di chuyển [origins × destinations], giá trị = mét.
    /// </summary>
    public double[,] Distances { get; set; } = new double[0, 0];
}
