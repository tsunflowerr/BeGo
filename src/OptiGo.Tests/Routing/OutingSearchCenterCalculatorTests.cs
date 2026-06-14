using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Services;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Tests.Routing;

public class OutingSearchCenterCalculatorTests
{
    [Fact]
    public void CarPickupGroupAndFarMotorbikeUseMidpointInsteadOfFarEndpoint()
    {
        var session = new Session("host");
        var driver = new Member(
            session.Id,
            "Driver",
            new Coordinate(21.0326, 105.7863),
            TransportMode.Car,
            MemberMobilityRole.SelfTravel);
        var motorbike = new Member(
            session.Id,
            "Motorbike",
            new Coordinate(21.0162, 105.8237),
            TransportMode.Motorbike,
            MemberMobilityRole.SelfTravel);
        var passenger = new Member(
            session.Id,
            "Passenger",
            new Coordinate(21.0401, 105.7921),
            TransportMode.Walking,
            MemberMobilityRole.NeedsPickup);
        passenger.SetDriver(driver.Id);

        session.AddMember(driver);
        session.AddMember(motorbike);
        session.AddMember(passenger);

        var center = OutingSearchCenterCalculator.Calculate(session);
        var groupDistance = driver.GetLocation().DistanceTo(motorbike.GetLocation());

        Assert.True(center.DistanceTo(motorbike.GetLocation()) > groupDistance * 0.2);
        Assert.True(center.DistanceTo(driver.GetLocation()) > groupDistance * 0.2);
    }

    [Fact]
    public void PendingPickupPassengerWithAvailableDriverUsesProjectedPickupBias()
    {
        var session = new Session("host");
        var driverA = new Member(
            session.Id,
            "Driver A",
            new Coordinate(21.0326, 105.7863),
            TransportMode.Motorbike,
            MemberMobilityRole.SelfTravel);
        var driverB = new Member(
            session.Id,
            "Driver B",
            new Coordinate(21.0075, 105.8529),
            TransportMode.Motorbike,
            MemberMobilityRole.SelfTravel);
        var passenger = new Member(
            session.Id,
            "Passenger",
            new Coordinate(21.0382, 105.7863),
            TransportMode.Walking,
            MemberMobilityRole.NeedsPickup);

        session.AddMember(driverA);
        session.AddMember(driverB);
        session.AddMember(passenger);
        session.CreateOrGetPickupRequest(passenger.Id);

        var weightedCenter = OutingSearchCenterCalculator.Calculate(session);
        var driverDistance = driverA.GetLocation().DistanceTo(driverB.GetLocation());

        Assert.True(weightedCenter.DistanceTo(driverA.GetLocation()) > driverDistance * 0.15);
        Assert.True(weightedCenter.DistanceTo(driverB.GetLocation()) > driverDistance * 0.15);
        Assert.True(weightedCenter.DistanceTo(passenger.GetLocation()) > 100);
    }

    [Fact]
    public void MatchedPickupPassengerAppliesLightBiasWithoutOverridingFarMember()
    {
        var session = new Session("host");
        var driverA = new Member(
            session.Id,
            "Driver A",
            new Coordinate(21.0326, 105.7863),
            TransportMode.Motorbike,
            MemberMobilityRole.SelfTravel);
        var driverB = new Member(
            session.Id,
            "Driver B",
            new Coordinate(21.0075, 105.8529),
            TransportMode.Motorbike,
            MemberMobilityRole.SelfTravel);
        var passenger = new Member(
            session.Id,
            "Passenger",
            new Coordinate(21.0382, 105.7863),
            TransportMode.Walking,
            MemberMobilityRole.NeedsPickup);
        passenger.SetDriver(driverA.Id);

        session.AddMember(driverA);
        session.AddMember(driverB);
        session.AddMember(passenger);

        var weightedCenter = OutingSearchCenterCalculator.Calculate(session);
        var driverDistance = driverA.GetLocation().DistanceTo(driverB.GetLocation());

        Assert.True(weightedCenter.DistanceTo(driverA.GetLocation()) > driverDistance * 0.15);
        Assert.True(weightedCenter.DistanceTo(driverB.GetLocation()) > driverDistance * 0.15);
        Assert.True(weightedCenter.DistanceTo(passenger.GetLocation()) > 100);
    }
}
