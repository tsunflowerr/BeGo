using System;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Entities;

public class Venue
{
    public string Id { get; private set; } = null!; // Mapbox/Google Place ID
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public double Rating { get; private set; }
    public int ReviewCount { get; private set; }
    public int? PriceLevel { get; private set; }
    public string? Address { get; private set; }
    public DateTime CachedAt { get; private set; }

    private Venue() { }

    public Venue(string id, string name, string category, Coordinate location,
        double rating, int reviewCount = 0, int? priceLevel = null, string? address = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Latitude = location.Latitude;
        Longitude = location.Longitude;
        Rating = rating;
        ReviewCount = reviewCount;
        PriceLevel = priceLevel;
        Address = address;
        CachedAt = DateTime.UtcNow;
    }

    public Coordinate GetLocation() => new(Latitude, Longitude);
}
