using System;
using OptiGo.Domain.ValueObjects;
using OptiGo.Domain.Enums;

namespace OptiGo.Domain.Entities;

public class Member
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public string Name { get; private set; } = null!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public TransportMode TransportMode { get; private set; }
    public MemberMobilityRole MobilityRole { get; private set; }
    public Guid? DriverId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public Session? Session { get; private set; }

    private Member() { }

    public Member(
        Guid sessionId,
        string name,
        Coordinate location,
        TransportMode transportMode = TransportMode.Motorbike,
        MemberMobilityRole mobilityRole = MemberMobilityRole.SelfTravel)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Latitude = location.Latitude;
        Longitude = location.Longitude;
        TransportMode = transportMode;
        MobilityRole = mobilityRole;
        JoinedAt = DateTime.UtcNow;
    }

    public Coordinate GetLocation()
    {
        return new Coordinate(Latitude, Longitude);
    }

    public void UpdateLocation(Coordinate newLocation)
    {
        Latitude = newLocation.Latitude;
        Longitude = newLocation.Longitude;
    }

    public void ChangeTransportMode(TransportMode newMode)
    {
        TransportMode = newMode;
    }

    public void UpdateMobility(MemberMobilityRole role, TransportMode transportMode)
    {
        MobilityRole = role;
        TransportMode = transportMode;

        if (role != MemberMobilityRole.NeedsPickup)
        {
            DriverId = null;
        }
    }

    public void SetDriver(Guid driverId)
    {
        if (driverId == Id)
            throw new ArgumentException("Cannot set self as driver.");
        DriverId = driverId;
    }

    public void RemoveDriver()
    {
        DriverId = null;
    }

    public bool IsPassenger() => DriverId.HasValue;

    public bool NeedsPickup() => MobilityRole == MemberMobilityRole.NeedsPickup;

    public bool CanOfferPickup() =>
        MobilityRole == MemberMobilityRole.SelfTravel &&
        TransportCapacityDefaults.GetSeatCapacity(TransportMode) > 0;

    public int GetSeatCapacity() => TransportCapacityDefaults.GetSeatCapacity(TransportMode);
}
