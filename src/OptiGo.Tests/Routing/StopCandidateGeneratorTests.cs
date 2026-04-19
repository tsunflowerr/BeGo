using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class StopCandidateGeneratorTests
{
    [Fact]
    public async Task GeneratesMergedCandidateForNearbyPassengers()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var first = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0010, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var second = TestRoutingSupport.CreateMember(sessionId, "B", 0, 0.0018, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.02), 4.6, 200);
        var generator = new StopCandidateGenerator();

        var candidates = await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [first, second],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        var merged = Assert.Single(candidates.Where(candidate => candidate.IsMergedStop));
        Assert.Contains(first.Id, merged.PassengerIds);
        Assert.Contains(second.Id, merged.PassengerIds);
    }
}
