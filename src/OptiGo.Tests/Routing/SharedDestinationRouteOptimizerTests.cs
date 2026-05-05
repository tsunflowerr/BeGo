using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class SharedDestinationRouteOptimizerTests
{
    [Fact]
    public async Task UsesCumulativeDistanceForPassengerRideDistance()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passengerA = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0180, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var passengerB = TestRoutingSupport.CreateMember(sessionId, "B", 0, 0.0450, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Venue", "cafe", new Coordinate(0, 0.0900), 4.5, 100);

        var optimizer = new SharedDestinationRouteOptimizer(
            new FixedStopCandidateGenerator([
                new StopCandidate
                {
                    CandidateId = "A-door",
                    StopLocation = passengerA.GetLocation(),
                    Label = "A",
                    StopAccessType = "doorstep",
                    PassengerIds = [passengerA.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passengerA.Id] = 0 }
                },
                new StopCandidate
                {
                    CandidateId = "B-door",
                    StopLocation = passengerB.GetLocation(),
                    Label = "B",
                    StopAccessType = "doorstep",
                    PassengerIds = [passengerB.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passengerB.Id] = 0 }
                }
            ]),
            new FakeRouteCostProvider());

        var result = await optimizer.OptimizeAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passengerA, passengerB],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        var passengerRoute = Assert.Single(result.PassengerRoutes.Where(route => route.MemberId == passengerB.Id));
        Assert.InRange(passengerRoute.RideDistanceMeters, 4900, 5300);
    }

    [Fact]
    public async Task AddsPickupServiceTimeToDriverRoute()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Venue", "cafe", new Coordinate(0, 0.0200), 4.5, 100);
        var routeCostProvider = new FakeRouteCostProvider();

        var optimizer = new SharedDestinationRouteOptimizer(
            new FixedStopCandidateGenerator([
                new StopCandidate
                {
                    CandidateId = "A-door",
                    StopLocation = passenger.GetLocation(),
                    Label = "A",
                    StopAccessType = "doorstep",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = 0 }
                }
            ]),
            routeCostProvider);

        var result = await optimizer.OptimizeAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        var withoutServiceSeconds =
            (driver.GetLocation().DistanceTo(passenger.GetLocation()) +
             passenger.GetLocation().DistanceTo(venue.GetLocation())) / 7.5;

        Assert.InRange(result.DriverRoute.TotalTimeSeconds - withoutServiceSeconds, 89, 91);
        var pickupStop = Assert.Single(result.DriverRoute.Stops.Where(stop => stop.StopType == "pickup"));
        Assert.Equal(90, pickupStop.ServiceTimeSeconds);
    }

    [Fact]
    public async Task ExposesRoutePolylineForMapRendering()
    {
        var sessionId = Guid.NewGuid();
        var driver = TestRoutingSupport.CreateMember(sessionId, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(sessionId, "A", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var venue = new Venue("v1", "Venue", "cafe", new Coordinate(0, 0.0200), 4.5, 100);

        var optimizer = new SharedDestinationRouteOptimizer(
            new FixedStopCandidateGenerator([
                new StopCandidate
                {
                    CandidateId = "A-door",
                    StopLocation = passenger.GetLocation(),
                    Label = "A",
                    StopAccessType = "doorstep",
                    PassengerIds = [passenger.Id],
                    WalkingDistancesMeters = new Dictionary<Guid, double> { [passenger.Id] = 0 }
                }
            ]),
            new FakeRouteCostProvider());

        var result = await optimizer.OptimizeAsync(new DriverOptimizationInput
        {
            Driver = driver,
            Passengers = [passenger],
            Venue = venue,
            TrafficSnapshot = new TrafficSnapshot("test")
        });

        Assert.True(result.DriverRoute.RoutePolyline.Count >= 3);
        Assert.Equal(driver.Latitude, result.DriverRoute.RoutePolyline.First().Latitude, 6);
        Assert.Equal(venue.Longitude, result.DriverRoute.RoutePolyline.Last().Longitude, 6);
    }
}
