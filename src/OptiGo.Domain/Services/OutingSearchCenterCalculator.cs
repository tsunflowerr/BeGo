using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class OutingSearchCenterCalculator
{
    public static Coordinate Calculate(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var members = session.Members.ToList();
        if (members.Count == 0)
            throw new ArgumentException("Session must contain at least one member.", nameof(session));

        return WeightedGeometricMedianCalculator.Calculate(members);
    }
}
