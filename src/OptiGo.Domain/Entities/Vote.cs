using System;

namespace OptiGo.Domain.Entities;

public class Vote
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid MemberId { get; private set; }
    public string VenueId { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    // Navigation properties cho EF Core
    public Session? Session { get; private set; }
    public Member? Member { get; private set; }

    private Vote() { }

    public Vote(Guid sessionId, Guid memberId, string venueId)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        MemberId = memberId;
        VenueId = venueId ?? throw new ArgumentNullException(nameof(venueId));
        CreatedAt = DateTime.UtcNow;
    }
}
