using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class RouteAwareVenuePrefilterTests
{
    [Fact]
    public async Task KeepsDriverClusterFriendlyVenue()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passengerA = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var passengerB = TestRoutingSupport.CreateMember(sessionId, "B", 0, 0.0110, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var session = TestRoutingSupport.CreateSessionWithAcceptedRequests(driver, passengerA, passengerB);
        var prefilter = new RouteAwareVenuePrefilter(new FakeRouteCostProvider(), new FakeTrafficSnapshotProvider());

        var clusterFriendly = new Venue("cluster", "Cluster", "cafe", new Coordinate(0, 0.0450), 4.1, 50);
        var outlier = new Venue("outlier", "Outlier", "cafe", new Coordinate(0.0300, 0.0050), 4.9, 1200);
        var far = new Venue("far", "Far", "cafe", new Coordinate(0.0400, 0.0700), 4.0, 10);

        var filtered = await prefilter.FilterTopCandidatesAsync(session, [clusterFriendly, outlier, far], 2);

        Assert.Contains(filtered, venue => venue.Id == "cluster");
    }
}
