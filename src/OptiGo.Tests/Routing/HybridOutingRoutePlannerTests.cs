using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class HybridOutingRoutePlannerTests
{
    [Fact]
    public async Task PlanVenueAsyncAutomaticallyAssignsPendingPickupPassenger()
    {
        var session = new Session("host");
        var driver = TestRoutingSupport.CreateMember(session.Id, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(session.Id, "Passenger", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(driver);
        session.AddMember(passenger);
        session.CreateOrGetPickupRequest(passenger.Id);

        var planner = CreatePlanner();
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0300), 4.5, 100);

        var result = await planner.PlanVenueAsync(session, venue);

        var passengerRoute = Assert.Single(result.MemberRoutes.Where(route => route.MemberId == passenger.Id));
        Assert.Equal(driver.Id, passengerRoute.DriverId);
        Assert.Contains(passenger.Id, Assert.Single(result.DriverRoutes).PassengerIds);
    }

    [Fact]
    public async Task PlanVenueAsyncKeepsAcceptedDriverAsHardAssignment()
    {
        var session = new Session("host");
        var acceptedDriver = TestRoutingSupport.CreateMember(session.Id, "Accepted", 0, 0.0300, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var closerDriver = TestRoutingSupport.CreateMember(session.Id, "Closer", 0, 0.0080, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(session.Id, "Passenger", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(acceptedDriver);
        session.AddMember(closerDriver);
        session.AddMember(passenger);
        var request = session.CreateOrGetPickupRequest(passenger.Id);
        session.AcceptPickupRequest(request.Id, acceptedDriver.Id);

        var planner = CreatePlanner();
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0400), 4.5, 100);

        var result = await planner.PlanVenueAsync(session, venue);

        var passengerRoute = Assert.Single(result.MemberRoutes.Where(route => route.MemberId == passenger.Id));
        Assert.Equal(acceptedDriver.Id, passengerRoute.DriverId);
    }

    [Fact]
    public async Task PlanVenueAsyncRespectsMotorbikeCapacityWhenAutoAssigning()
    {
        var session = new Session("host");
        var driverA = TestRoutingSupport.CreateMember(session.Id, "Driver A", 0, 0, TransportMode.Motorbike, MemberMobilityRole.SelfTravel);
        var driverB = TestRoutingSupport.CreateMember(session.Id, "Driver B", 0, 0.0200, TransportMode.Motorbike, MemberMobilityRole.SelfTravel);
        var passengerA = TestRoutingSupport.CreateMember(session.Id, "Passenger A", 0, 0.0050, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var passengerB = TestRoutingSupport.CreateMember(session.Id, "Passenger B", 0, 0.0250, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(driverA);
        session.AddMember(driverB);
        session.AddMember(passengerA);
        session.AddMember(passengerB);
        session.CreateOrGetPickupRequest(passengerA.Id);
        session.CreateOrGetPickupRequest(passengerB.Id);

        var planner = CreatePlanner();
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0400), 4.5, 100);

        var result = await planner.PlanVenueAsync(session, venue);

        var pickedPassengerRoutes = result.MemberRoutes
            .Where(route => route.MemberId == passengerA.Id || route.MemberId == passengerB.Id)
            .ToList();
        Assert.All(result.DriverRoutes, route => Assert.True(route.PassengerIds.Count <= 1));
        Assert.Equal(2, pickedPassengerRoutes.Select(route => route.DriverId).Distinct().Count());
    }

    [Fact]
    public async Task PlanVenueAsyncUsesRoutePoolToSelectBestDriverSubset()
    {
        var session = new Session("host");
        var driverA = TestRoutingSupport.CreateMember(session.Id, "Driver A", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var driverB = TestRoutingSupport.CreateMember(session.Id, "Driver B", 0, 0.0300, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passengerA = TestRoutingSupport.CreateMember(session.Id, "Passenger A", 0, 0.0060, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        var passengerB = TestRoutingSupport.CreateMember(session.Id, "Passenger B", 0, 0.0070, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(driverA);
        session.AddMember(driverB);
        session.AddMember(passengerA);
        session.AddMember(passengerB);
        session.CreateOrGetPickupRequest(passengerA.Id);
        session.CreateOrGetPickupRequest(passengerB.Id);

        var planner = new HybridOutingRoutePlanner(
            new ControlledRouteOptimizer(driverA.Id, [passengerA.Id, passengerB.Id]),
            new FakeRouteCostProvider(),
            new FakeTrafficSnapshotProvider());
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.5, 100);

        var result = await planner.PlanVenueAsync(session, venue);

        var driverARoute = Assert.Single(result.DriverRoutes.Where(route => route.DriverId == driverA.Id));
        var driverBRoute = Assert.Single(result.DriverRoutes.Where(route => route.DriverId == driverB.Id));
        Assert.Equal(2, driverARoute.PassengerIds.Count);
        Assert.Empty(driverBRoute.PassengerIds);
        Assert.All([passengerA.Id, passengerB.Id], passengerId =>
            Assert.Single(result.MemberRoutes.Where(route => route.MemberId == passengerId)));
    }

    [Fact]
    public async Task PlanVenueAsyncMarksRouteInfeasibleWhenPassengerTimeExceedsLimit()
    {
        var session = new Session("host");
        var driver = TestRoutingSupport.CreateMember(session.Id, "Driver", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(session.Id, "Passenger", 0, 0.0100, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(driver);
        session.AddMember(passenger);
        session.CreateOrGetPickupRequest(passenger.Id);

        var planner = new HybridOutingRoutePlanner(
            new SlowPassengerRouteOptimizer(),
            new FakeRouteCostProvider(),
            new FakeTrafficSnapshotProvider());
        var venue = new Venue("v1", "Cafe", "cafe", new Coordinate(0, 0.0200), 4.5, 100);

        var result = await planner.PlanVenueAsync(session, venue);

        Assert.False(result.IsFeasible);
        Assert.Contains(result.FeasibilityIssues, issue => issue.Contains("tổng thời gian", StringComparison.OrdinalIgnoreCase));
    }

    private static HybridOutingRoutePlanner CreatePlanner()
    {
        var routeCostProvider = new FakeRouteCostProvider();
        return new HybridOutingRoutePlanner(
            new SharedDestinationRouteOptimizer(new StopCandidateGenerator(), routeCostProvider),
            routeCostProvider,
            new FakeTrafficSnapshotProvider());
    }
}

internal sealed class ControlledRouteOptimizer : IDriverRouteOptimizer
{
    private readonly Guid _preferredDriverId;
    private readonly HashSet<Guid> _preferredPassengerIds;

    public ControlledRouteOptimizer(Guid preferredDriverId, IEnumerable<Guid> preferredPassengerIds)
    {
        _preferredDriverId = preferredDriverId;
        _preferredPassengerIds = preferredPassengerIds.ToHashSet();
    }

    public Task<DriverOptimizationResult> OptimizeAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default)
    {
        var passengerIds = input.Passengers.Select(passenger => passenger.Id).ToHashSet();
        var isPreferredBundle =
            input.Driver.Id == _preferredDriverId &&
            passengerIds.SetEquals(_preferredPassengerIds);
        var seconds = input.Passengers.Count == 0
            ? 20
            : isPreferredBundle
                ? 100
                : 900 + input.Passengers.Count * 100;

        return Task.FromResult(BuildResult(input, seconds));
    }

    private static DriverOptimizationResult BuildResult(DriverOptimizationInput input, double seconds)
    {
        var passengerRoutes = input.Passengers.Select(passenger => new MemberRouteDto
        {
            MemberId = passenger.Id,
            MemberName = passenger.Name,
            EstimatedTimeSeconds = seconds,
            DistanceMeters = seconds,
            RideTimeSeconds = seconds,
            RideDistanceMeters = seconds,
            DriverId = input.Driver.Id,
            BurdenScore = seconds
        }).ToList();

        return new DriverOptimizationResult
        {
            DriverRoute = new DriverRouteDto
            {
                DriverId = input.Driver.Id,
                DriverName = input.Driver.Name,
                TotalTimeSeconds = seconds,
                TotalDistanceMeters = seconds,
                DirectTimeSeconds = 20,
                DirectDistanceMeters = 20,
                GeneralizedCostSeconds = seconds,
                PassengerIds = input.Passengers.Select(passenger => passenger.Id).ToList(),
                Stops =
                [
                    new RouteStopDto
                    {
                        Sequence = 0,
                        StopType = "driver_origin",
                        Label = input.Driver.Name,
                        Latitude = input.Driver.Latitude,
                        Longitude = input.Driver.Longitude
                    },
                    new RouteStopDto
                    {
                        Sequence = 1,
                        StopType = "destination",
                        Label = input.Venue.Name,
                        Latitude = input.Venue.Latitude,
                        Longitude = input.Venue.Longitude,
                        EtaSeconds = seconds
                    }
                ]
            },
            PassengerRoutes = passengerRoutes,
            CostBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = seconds,
                TotalDriveSeconds = seconds
            }
        };
    }
}

internal sealed class SlowPassengerRouteOptimizer : IDriverRouteOptimizer
{
    public Task<DriverOptimizationResult> OptimizeAsync(
        DriverOptimizationInput input,
        CancellationToken ct = default)
    {
        const double seconds = 60 * 60;
        var passengerRoutes = input.Passengers.Select(passenger => new MemberRouteDto
        {
            MemberId = passenger.Id,
            MemberName = passenger.Name,
            EstimatedTimeSeconds = seconds,
            DistanceMeters = seconds,
            RideTimeSeconds = seconds,
            RideDistanceMeters = seconds,
            DriverId = input.Driver.Id,
            BurdenScore = seconds
        }).ToList();

        return Task.FromResult(new DriverOptimizationResult
        {
            DriverRoute = new DriverRouteDto
            {
                DriverId = input.Driver.Id,
                DriverName = input.Driver.Name,
                TotalTimeSeconds = input.Passengers.Count == 0 ? 60 : seconds,
                TotalDistanceMeters = input.Passengers.Count == 0 ? 60 : seconds,
                DirectTimeSeconds = 60,
                DirectDistanceMeters = 60,
                GeneralizedCostSeconds = input.Passengers.Count == 0 ? 60 : seconds,
                PassengerIds = input.Passengers.Select(passenger => passenger.Id).ToList(),
                Stops =
                [
                    new RouteStopDto
                    {
                        Sequence = 0,
                        StopType = "driver_origin",
                        Label = input.Driver.Name,
                        Latitude = input.Driver.Latitude,
                        Longitude = input.Driver.Longitude
                    },
                    new RouteStopDto
                    {
                        Sequence = 1,
                        StopType = "destination",
                        Label = input.Venue.Name,
                        Latitude = input.Venue.Latitude,
                        Longitude = input.Venue.Longitude,
                        EtaSeconds = seconds
                    }
                ]
            },
            PassengerRoutes = passengerRoutes,
            CostBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = input.Passengers.Count == 0 ? 60 : seconds,
                TotalDriveSeconds = input.Passengers.Count == 0 ? 60 : seconds
            }
        });
    }
}
