using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IVenueRepository
{
    Task<Venue?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Venue>> GetByCategoryInAreaAsync(
        string category, double centerLat, double centerLng, double radiusMeters,
        CancellationToken ct = default);
    Task AddOrUpdateAsync(Venue venue, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Venue> venues, CancellationToken ct = default);
}
