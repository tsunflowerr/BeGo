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

        var acceptedRequests = session.PickupRequests
            .Where(request => request.IsAccepted())
            .ToList();

        if (acceptedRequests.Count == 0)
            return WeightedGeometricMedianCalculator.Calculate(members);

        var memberById = members.ToDictionary(member => member.Id);
        var points = new List<WeightedAnchor>();

        foreach (var driver in members.Where(member => member.CanOfferPickup()))
        {
            points.Add(new WeightedAnchor(driver.GetLocation(), 1.35));
        }

        foreach (var request in acceptedRequests)
        {
            if (!request.AcceptedDriverId.HasValue)
                continue;

            var passenger = memberById[request.PassengerId];
            var driver = memberById[request.AcceptedDriverId.Value];
            points.Add(new WeightedAnchor(
                Blend(passenger.GetLocation(), driver.GetLocation(), 0.58),
                0.8));
        }

        foreach (var member in members.Where(member => !member.CanOfferPickup() && !member.NeedsPickup()))
        {
            points.Add(new WeightedAnchor(member.GetLocation(), 1.0));
        }

        if (points.Count == 0)
            return WeightedGeometricMedianCalculator.Calculate(members);

        var totalWeight = points.Sum(point => point.Weight);
        var latitude = points.Sum(point => point.Point.Latitude * point.Weight) / totalWeight;
        var longitude = points.Sum(point => point.Point.Longitude * point.Weight) / totalWeight;
        return new Coordinate(latitude, longitude);
    }

    private static Coordinate Blend(Coordinate from, Coordinate to, double ratio) =>
        new(
            from.Latitude + (to.Latitude - from.Latitude) * ratio,
            from.Longitude + (to.Longitude - from.Longitude) * ratio);

    private readonly record struct WeightedAnchor(Coordinate Point, double Weight);
}
