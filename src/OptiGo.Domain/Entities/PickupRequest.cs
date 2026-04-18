using OptiGo.Domain.Enums;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Domain.Entities;

public class PickupRequest
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid PassengerId { get; private set; }
    public Guid? AcceptedDriverId { get; private set; }
    public PickupRequestStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public Session? Session { get; private set; }
    public Member? Passenger { get; private set; }
    public Member? AcceptedDriver { get; private set; }

    private PickupRequest()
    {
    }

    public PickupRequest(Guid sessionId, Guid passengerId)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        PassengerId = passengerId;
        Status = PickupRequestStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void Accept(Guid driverId)
    {
        if (Status == PickupRequestStatus.Cancelled)
            throw new DomainException("Cannot accept a cancelled pickup request.");

        AcceptedDriverId = driverId;
        Status = PickupRequestStatus.Accepted;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Release()
    {
        if (Status == PickupRequestStatus.Cancelled)
            throw new DomainException("Cannot release a cancelled pickup request.");

        AcceptedDriverId = null;
        Status = PickupRequestStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        AcceptedDriverId = null;
        Status = PickupRequestStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsPending() => Status == PickupRequestStatus.Pending;

    public bool IsAccepted() => Status == PickupRequestStatus.Accepted && AcceptedDriverId.HasValue;
}
