using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Domain.Services;

public static class PickupPairValidator
{
    public static void ValidateOneToOnePairs(IReadOnlyList<Member> members)
    {
        var memberById = members.ToDictionary(member => member.Id);
        var passengers = members.Where(member => member.DriverId.HasValue).ToList();

        foreach (var passenger in passengers)
        {
            var driverId = passenger.DriverId!.Value;

            if (!memberById.ContainsKey(driverId))
                throw new DomainException($"Driver {driverId} does not exist in this session.");

            if (driverId == passenger.Id)
                throw new DomainException("A member cannot be their own driver.");
        }

        var duplicateDrivers = passengers
            .GroupBy(passenger => passenger.DriverId!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateDrivers.Count > 0)
        {
            throw new DomainException(
                $"Each driver can pick up at most one passenger. Conflicting drivers: {string.Join(", ", duplicateDrivers)}");
        }

        foreach (var passenger in passengers)
        {
            var driver = memberById[passenger.DriverId!.Value];
            if (driver.DriverId.HasValue)
                throw new DomainException("A driver cannot also be a passenger in another pickup pair.");
        }

        foreach (var member in members)
        {
            var visited = new HashSet<Guid>();
            var current = member;

            while (current.DriverId.HasValue)
            {
                if (!visited.Add(current.Id))
                    throw new DomainException("Pickup assignments contain a cycle.");

                current = memberById[current.DriverId.Value];
            }
        }
    }
}
