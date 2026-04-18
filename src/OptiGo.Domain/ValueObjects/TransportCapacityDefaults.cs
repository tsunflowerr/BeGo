using OptiGo.Domain.Enums;

namespace OptiGo.Domain.ValueObjects;

public static class TransportCapacityDefaults
{
    public static int GetSeatCapacity(TransportMode mode) => mode switch
    {
        TransportMode.Motorbike => 1,
        TransportMode.Car => 3,
        TransportMode.Bus => 10,
        _ => 0
    };
}
