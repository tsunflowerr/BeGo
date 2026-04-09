using OptiGo.Domain.Enums;

namespace OptiGo.Infrastructure.ExternalServices.Mapbox;

public static class MapboxTransportModeMapper
{

    public static string ToMapboxProfile(TransportMode mode) => mode switch
    {
        TransportMode.Walking  => "walking",
        TransportMode.Cycling  => "cycling",
        TransportMode.Motorbike => "driving",
        TransportMode.Car      => "driving",
        TransportMode.Bus      => "driving",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown transport mode")
    };

    public static double GetAdjustmentFactor(TransportMode mode) => mode switch
    {
        TransportMode.Walking   => 1.0,
        TransportMode.Cycling   => 1.0,
        TransportMode.Motorbike => 0.85,
        TransportMode.Car       => 1.0,
        TransportMode.Bus       => 1.8,
        _ => 1.0
    };
}
