using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class HybridPlannerBenchmarkTests
{
    [Fact]
    public async Task HybridPlannerImprovesGeneralizedCostAgainstBaseline()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passengerA = TestRoutingSupport.CreateMember(sessionId, "A", 0.0001, 0.0120, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var passengerB = TestRoutingSupport.CreateMember(sessionId, "B", -0.0001, 0.0126, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Venue", "cafe", new Coordinate(0, 0.0600), 4.7, 320);
        var session = TestRoutingSupport.CreateSessionWithAcceptedRequests(driver, passengerA, passengerB);

        var routeCostProvider = new FakeRouteCostProvider();
        var trafficProvider = new FakeTrafficSnapshotProvider();
        var baseline = new BaselineOutingRoutePlanner(routeCostProvider, trafficProvider);
        var hybrid = new HybridOutingRoutePlanner(
            new SharedDestinationRouteOptimizer(
                new FixedStopCandidateGenerator([
                    new StopCandidate
                    {
                        CandidateId = "A-door",
                        StopLocation = passengerA.GetLocation(),
                        Label = "A-door",
                        StopAccessType = "doorstep",
                        PassengerIds = [passengerA.Id],
                        WalkingDistancesMeters = new Dictionary<Guid, double> { [passengerA.Id] = 0 }
                    },
                    new StopCandidate
                    {
                        CandidateId = "B-door",
                        StopLocation = passengerB.GetLocation(),
                        Label = "B-door",
                        StopAccessType = "doorstep",
                        PassengerIds = [passengerB.Id],
                        WalkingDistancesMeters = new Dictionary<Guid, double> { [passengerB.Id] = 0 }
                    },
                    new StopCandidate
                    {
                        CandidateId = "AB-merged",
                        StopLocation = new Coordinate(0, 0.0123),
                        Label = "AB-merged",
                        StopAccessType = "shared_meetpoint",
                        PassengerIds = [passengerA.Id, passengerB.Id],
                        WalkingDistancesMeters = new Dictionary<Guid, double>
                        {
                            [passengerA.Id] = 0,
                            [passengerB.Id] = 0
                        }
                    }
                ]),
                routeCostProvider),
            routeCostProvider,
            trafficProvider);

        var baselineResult = await baseline.PlanVenueAsync(session, venue);
        var hybridResult = await hybrid.PlanVenueAsync(session, venue);

        Assert.True(
            hybridResult.ScoreBreakdown.GeneralizedCostSeconds < baselineResult.ScoreBreakdown.GeneralizedCostSeconds,
            $"Expected hybrid cost {hybridResult.ScoreBreakdown.GeneralizedCostSeconds} to be lower than baseline {baselineResult.ScoreBreakdown.GeneralizedCostSeconds}.");
    }

    [Fact]
    public async Task RouteAwarePrefilterKeepsDriverClusterFriendlyVenue()
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
