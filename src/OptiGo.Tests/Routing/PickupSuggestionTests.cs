using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Enums;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Tests.Routing;

public class PickupSuggestionTests
{
    [Fact]
    public async Task SuggestsBestAvailableDriverForPendingPickup()
    {
        var session = new Session("host");
        var closerDriver = TestRoutingSupport.CreateMember(session.Id, "Closer", 0, 0, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var fartherDriver = TestRoutingSupport.CreateMember(session.Id, "Farther", 0, 0.0600, TransportMode.Car, MemberMobilityRole.SelfTravel);
        var passenger = TestRoutingSupport.CreateMember(session.Id, "Passenger", 0, 0.0050, TransportMode.Walking, MemberMobilityRole.NeedsPickup);
        session.AddMember(closerDriver);
        session.AddMember(fartherDriver);
        session.AddMember(passenger);
        session.CreateOrGetPickupRequest(passenger.Id);

        var handler = new GetPickupSuggestionsHandler(
            new InMemorySessionRepository(session),
            new FakeRouteCostProvider(),
            new FakeTrafficSnapshotProvider());

        var suggestions = await handler.Handle(new GetPickupSuggestionsQuery(session.Id), CancellationToken.None);

        Assert.NotEmpty(suggestions);
        var first = suggestions.First();
        Assert.Equal(passenger.Id, first.PassengerId);
        Assert.Equal(closerDriver.Id, first.DriverId);
        Assert.True(first.RemainingSeatCount > 0);
    }
}

internal sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly Session _session;

    public InMemorySessionRepository(Session session)
    {
        _session = session;
    }

    public Task<Session?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(id == _session.Id ? _session : null);

    public Task<Session?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(id == _session.Id ? _session : null);

    public Task AddAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(id == _session.Id);
}
