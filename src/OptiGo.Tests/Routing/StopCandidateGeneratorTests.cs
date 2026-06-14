using OptiGo.Application.UseCases;
using OptiGo.Application.Interfaces;
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

        Assert.Contains(candidates.Where(candidate => candidate.IsMergedStop), candidate =>
            candidate.PassengerIds.Contains(first.Id) &&
            candidate.PassengerIds.Contains(second.Id));
    }

    [Fact]
    public async Task GeneratesCorridorCandidateForPassengerNearDriverVenuePath()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0.0010, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.6, 200);
        var generator = new StopCandidateGenerator();

        var candidates = await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        var corridor = Assert.Single(candidates.Where(candidate => candidate.StopAccessType == "driver_corridor"));
        Assert.Equal(passenger.Id, Assert.Single(corridor.PassengerIds));
        Assert.InRange(corridor.WalkingDistancesMeters[passenger.Id], 90, 130);
    }

    [Fact]
    public async Task GeneratesClusterCandidateForThreeNearbyPassengers()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var first = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0010, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var second = TestRoutingSupport.CreateMember(sessionId, "B", 0.0002, 0.0014, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var third = TestRoutingSupport.CreateMember(sessionId, "C", -0.0002, 0.0012, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.6, 200);
        var generator = new StopCandidateGenerator();

        var candidates = await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [first, second, third],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        Assert.Contains(candidates, candidate =>
            candidate.StopAccessType == "shared_cluster_meetpoint" &&
            candidate.PassengerIds.Count == 3);
    }

    [Fact]
    public async Task AddsPoiLandmarkCandidateWhenProviderReturnsWalkablePoint()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0010, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.6, 200);
        var generator = new StopCandidateGenerator(new FixedMeetingPointProvider([
            new MeetingPointCandidate
            {
                Id = "poi-1",
                Name = "Circle K Nguyen Trai",
                Category = "convenience_store",
                Location = new Coordinate(0, 0.0015),
                PickupFriendlyScore = 0.95
            }
        ]));

        var candidates = await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        var poi = Assert.Single(candidates.Where(candidate => candidate.StopAccessType == "poi_landmark"));
        Assert.Contains("Circle K", poi.Label);
        Assert.InRange(poi.WalkingDistancesMeters[passenger.Id], 50, 60);
    }

    [Fact]
    public async Task DropsPoiLandmarkCandidateOutsideWalkingLimit()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0010, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.6, 200);
        var generator = new StopCandidateGenerator(new FixedMeetingPointProvider([
            new MeetingPointCandidate
            {
                Id = "poi-far",
                Name = "Far Landmark",
                Category = "cafe",
                Location = new Coordinate(0, 0.0100),
                PickupFriendlyScore = 0.95
            }
        ]));

        var candidates = await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        Assert.DoesNotContain(candidates, candidate => candidate.StopAccessType == "poi_landmark");
    }

    [Fact]
    public async Task ReusesPoiLookupForSamePassengerAcrossVenuePlans()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0010, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var firstVenue = new Venue("v1", "Cafe 1", "cafe", new Coordinate(0, 0.0200), 4.6, 200);
        var secondVenue = new Venue("v2", "Cafe 2", "cafe", new Coordinate(0.0100, 0.0200), 4.4, 150);
        var provider = new FixedMeetingPointProvider([
            new MeetingPointCandidate
            {
                Id = "poi-1",
                Name = "Circle K Nguyen Trai",
                Category = "convenience_store",
                Location = new Coordinate(0, 0.0015),
                PickupFriendlyScore = 0.95
            }
        ]);
        var generator = new StopCandidateGenerator(provider);

        await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = firstVenue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        await generator.GenerateAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = secondVenue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        Assert.Equal(1, provider.CallCount);
    }
}

internal sealed class FixedMeetingPointProvider : IMeetingPointProvider
{
    private readonly IReadOnlyList<MeetingPointCandidate> _candidates;
    private int _callCount;

    public int CallCount => _callCount;

    public FixedMeetingPointProvider(IReadOnlyList<MeetingPointCandidate> candidates)
    {
        _candidates = candidates;
    }

    public Task<IReadOnlyList<MeetingPointCandidate>> SearchPickupPointsAsync(
        Coordinate passengerLocation,
        double radiusMeters,
        int limit = 16,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(_candidates);
    }
}
