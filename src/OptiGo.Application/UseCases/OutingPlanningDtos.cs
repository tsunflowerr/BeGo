using OptiGo.Domain.Enums;

namespace OptiGo.Application.UseCases;

public class PickupRequestDto
{
    public Guid RequestId { get; init; }
    public Guid PassengerId { get; init; }
    public string PassengerName { get; init; } = string.Empty;
    public PickupRequestStatus Status { get; init; }
    public Guid? AcceptedDriverId { get; init; }
    public string? AcceptedDriverName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public class DriverRouteDto
{
    public Guid DriverId { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public double TotalTimeSeconds { get; init; }
    public double TotalDistanceMeters { get; init; }
    public double DirectTimeSeconds { get; init; }
    public double DirectDistanceMeters { get; init; }
    public double GeneralizedCostSeconds { get; init; }
    public List<Guid> PassengerIds { get; init; } = new();
    public List<RouteStopDto> Stops { get; init; } = new();
}

public class RouteStopDto
{
    public int Sequence { get; init; }
    public string StopType { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double EtaSeconds { get; init; }
    public double DistanceFromPreviousMeters { get; init; }
    public double CumulativeDistanceMeters { get; init; }
    public double CumulativeTimeSeconds { get; init; }
    public double WalkingDistanceMeters { get; init; }
    public double WaitSeconds { get; init; }
    public string StopAccessType { get; init; } = string.Empty;
    public bool IsMergedStop { get; init; }
    public List<Guid> PassengerIds { get; init; } = new();
}
