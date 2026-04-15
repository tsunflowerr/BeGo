using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IPlacesProvider
{
    Task<IReadOnlyList<Venue>> SearchNearbyAsync(
        double latitude,
        double longitude,
        string category,
        double radiusMeters = 500,
        int limit = 50,
        CancellationToken ct = default);

    Task<PlaceDetailResult> GetPlaceDetailAsync(string placeId, CancellationToken ct = default);
}

public class PlaceDetailResult
{
    public string PlaceId { get; set; } = null!;
    public List<string> PhotoUrls { get; set; } = new();
    public string? AiReviewSummary { get; set; }
    public List<PlaceReview> Reviews { get; set; } = new();
}

public class PlaceReview
{
    public string AuthorName { get; set; } = null!;
    public double Rating { get; set; }
    public string Text { get; set; } = null!;
    public string? RelativeTime { get; set; }
    public string? AuthorPhotoUrl { get; set; }
}
