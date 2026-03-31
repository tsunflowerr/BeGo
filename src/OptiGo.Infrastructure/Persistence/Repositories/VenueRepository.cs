using Microsoft.EntityFrameworkCore;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence.Repositories;

public class VenueRepository : IVenueRepository
{
    private readonly OptiGoDbContext _db;

    public VenueRepository(OptiGoDbContext db)
    {
        _db = db;
    }

    public async Task<Venue?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _db.Venues.FindAsync(new object[] { id }, ct);
    }

    public async Task<IReadOnlyList<Venue>> GetByCategoryInAreaAsync(
        string category, double centerLat, double centerLng, double radiusMeters,
        CancellationToken ct = default)
    {
        // Chuyển radius từ mét sang khoảng tương đương độ (xấp xỉ)
        // 1 độ latitude ≈ 111,320m
        var latDelta = radiusMeters / 111_320.0;
        // 1 độ longitude ≈ 111,320 * cos(latitude) mét
        var lngDelta = radiusMeters / (111_320.0 * Math.Cos(centerLat * Math.PI / 180.0));

        var minLat = centerLat - latDelta;
        var maxLat = centerLat + latDelta;
        var minLng = centerLng - lngDelta;
        var maxLng = centerLng + lngDelta;

        // Bounding box pre-filter (rất nhanh nhờ B-tree index trên lat/lng)
        // Sau đó có thể refine bằng Haversine nếu cần chính xác hơn
        return await _db.Venues
            .Where(v => v.Category == category
                && v.Latitude >= minLat && v.Latitude <= maxLat
                && v.Longitude >= minLng && v.Longitude <= maxLng)
            .OrderByDescending(v => v.Rating)
            .Take(50) // Giới hạn để tránh quá tải
            .ToListAsync(ct);
    }

    public async Task AddOrUpdateAsync(Venue venue, CancellationToken ct = default)
    {
        var existing = await _db.Venues.FindAsync(new object[] { venue.Id }, ct);
        if (existing is null)
        {
            await _db.Venues.AddAsync(venue, ct);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(venue);
        }
    }

    public async Task AddRangeAsync(IEnumerable<Venue> venues, CancellationToken ct = default)
    {
        foreach (var venue in venues)
        {
            await AddOrUpdateAsync(venue, ct);
        }
    }
}
