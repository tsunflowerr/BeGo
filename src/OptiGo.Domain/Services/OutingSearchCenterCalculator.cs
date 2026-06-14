using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class OutingSearchCenterCalculator
{
    private const double MatchedPickupLocationWeight = 0.25;

    public static Coordinate Calculate(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var members = session.Members.ToList();
        if (members.Count == 0)
            throw new ArgumentException("Session must contain at least one member.", nameof(session));

        var pickupPassengersByDriver = AssignPickupPassengersToDrivers(session, members);

        var weightedPoints = members
            .Where(member => !member.NeedsPickup() || !IsCoveredByPickupDriver(member, pickupPassengersByDriver))
            .Select(member => new WeightedGeometricMedianCalculator.WeightedPoint(
                CalculateEffectiveOrigin(member, pickupPassengersByDriver),
                CalculateEffectiveWeight(member, pickupPassengersByDriver)))
            .ToList();

        if (weightedPoints.Count == 2)
            return CalculateWeightedMidpoint(weightedPoints[0], weightedPoints[1]);

        return WeightedGeometricMedianCalculator.Calculate(weightedPoints);
    }

    private static Dictionary<Guid, List<Member>> AssignPickupPassengersToDrivers(Session session, IReadOnlyList<Member> members)
    {
        var passengersByDriver = members
            .Where(member => member.NeedsPickup() && member.DriverId.HasValue)
            .GroupBy(member => member.DriverId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var drivers = members
            .Where(member => member.CanOfferPickup())
            .ToList();
        var remainingSeatsByDriver = drivers.ToDictionary(
            driver => driver.Id,
            driver => Math.Max(0, driver.GetSeatCapacity() - session.GetAcceptedPassengerCount(driver.Id)));

        var pendingPassengers = members
            .Where(member => member.NeedsPickup() && !member.DriverId.HasValue)
            .OrderBy(member => member.JoinedAt)
            .ToList();

        foreach (var passenger in pendingPassengers)
        {
            var driver = drivers
                .Where(candidate => candidate.Id != passenger.Id)
                .Where(candidate => remainingSeatsByDriver.TryGetValue(candidate.Id, out var seats) && seats > 0)
                .OrderBy(candidate => candidate.GetLocation().DistanceTo(passenger.GetLocation()))
                .FirstOrDefault();
            if (driver == null)
                continue;

            if (!passengersByDriver.TryGetValue(driver.Id, out var passengers))
            {
                passengers = new List<Member>();
                passengersByDriver[driver.Id] = passengers;
            }

            passengers.Add(passenger);
            remainingSeatsByDriver[driver.Id]--;
        }

        return passengersByDriver;
    }

    private static bool IsCoveredByPickupDriver(
        Member member,
        IReadOnlyDictionary<Guid, List<Member>> pickupPassengersByDriver) =>
        pickupPassengersByDriver.Values.Any(passengers => passengers.Any(passenger => passenger.Id == member.Id));

    private static Coordinate CalculateEffectiveOrigin(
        Member member,
        IReadOnlyDictionary<Guid, List<Member>> matchedPassengersByDriver)
    {
        var origin = member.GetLocation();
        if (!matchedPassengersByDriver.TryGetValue(member.Id, out var passengers) || passengers.Count == 0)
            return origin;

        var totalWeight = 1.0 + passengers.Count * MatchedPickupLocationWeight;
        var latitude = origin.Latitude;
        var longitude = origin.Longitude;

        foreach (var passenger in passengers)
        {
            latitude += passenger.Latitude * MatchedPickupLocationWeight;
            longitude += passenger.Longitude * MatchedPickupLocationWeight;
        }

        return new Coordinate(latitude / totalWeight, longitude / totalWeight);
    }

    private static double CalculateEffectiveWeight(
        Member member,
        IReadOnlyDictionary<Guid, List<Member>> pickupPassengersByDriver)
    {
        var weight = WeightedGeometricMedianCalculator.GetMemberWeight(member);
        if (pickupPassengersByDriver.TryGetValue(member.Id, out var passengers))
            weight += passengers.Count * MatchedPickupLocationWeight;

        return weight;
    }

    private static Coordinate CalculateWeightedMidpoint(
        WeightedGeometricMedianCalculator.WeightedPoint first,
        WeightedGeometricMedianCalculator.WeightedPoint second)
    {
        var totalWeight = first.Weight + second.Weight;
        return new Coordinate(
            (first.Point.Latitude * first.Weight + second.Point.Latitude * second.Weight) / totalWeight,
            (first.Point.Longitude * first.Weight + second.Point.Longitude * second.Weight) / totalWeight);
    }
}
