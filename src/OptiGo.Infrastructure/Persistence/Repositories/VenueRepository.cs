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

        var latDelta = radiusMeters / 111_320.0;

        var lngDelta = radiusMeters / (111_320.0 * Math.Cos(centerLat * Math.PI / 180.0));

        var minLat = centerLat - latDelta;
        var maxLat = centerLat + latDelta;
        var minLng = centerLng - lngDelta;
        var maxLng = centerLng + lngDelta;

        return await _db.Venues
            .Where(v => v.Category == category
                && v.Latitude >= minLat && v.Latitude <= maxLat
                && v.Longitude >= minLng && v.Longitude <= maxLng)
            .OrderByDescending(v => v.Rating)
            .Take(50)
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
