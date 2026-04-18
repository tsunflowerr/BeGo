using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Domain.Entities;

public class Session
{
    public Guid Id { get; private set; }
    public string HostName { get; private set; } = null!;
    public SessionStatus Status { get; private set; }
    public string? QueryText { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string? WinningVenueId { get; private set; }
    public string? LatestOptimizationSnapshotJson { get; private set; }
    public string? FinalRouteSnapshotJson { get; private set; }
    public DateTime? DepartureLockedAt { get; private set; }

    private readonly List<Member> _members = new();
    public IReadOnlyCollection<Member> Members => _members.AsReadOnly();

    private readonly List<Vote> _votes = new();
    public IReadOnlyCollection<Vote> Votes => _votes.AsReadOnly();

    private readonly List<string> _nominatedVenueIds = new();
    public IReadOnlyCollection<string> NominatedVenueIds => _nominatedVenueIds.AsReadOnly();

    private readonly List<PickupRequest> _pickupRequests = new();
    public IReadOnlyCollection<PickupRequest> PickupRequests => _pickupRequests.AsReadOnly();

    private Session() { }

    public Session(string hostName)
    {
        Id = Guid.NewGuid();
        HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
        Status = SessionStatus.WaitingForMembers;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = DateTime.UtcNow.AddHours(24);
    }

    public void SetQueryText(string queryText)
    {
        QueryText = queryText;
    }

    public void AddMember(Member member)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot add members after computation has started.");

        if (_members.Any(m => m.Id == member.Id))
            throw new DomainException("Member already exists in the session.");

        _members.Add(member);
    }

    public void RemoveMember(Guid memberId)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot remove members after computation has started.");

        var member = _members.FirstOrDefault(m => m.Id == memberId);
        if (member != null)
        {
            var pickupRequest = _pickupRequests.FirstOrDefault(r => r.PassengerId == memberId);
            if (pickupRequest != null)
            {
                _pickupRequests.Remove(pickupRequest);
            }

            _members.Remove(member);
        }
    }

    public void SetMemberDriver(Guid memberId, Guid? driverId)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot update pickup assignments after computation has started.");

        var member = _members.FirstOrDefault(m => m.Id == memberId);
        if (member == null)
            throw new DomainException("Member not found in the session.");

        if (!driverId.HasValue)
        {
            var request = CreateOrGetPickupRequest(member.Id);
            request.Release();
            member.RemoveDriver();
            return;
        }

        var pickupRequest = CreateOrGetPickupRequest(member.Id);
        AcceptPickupRequest(pickupRequest.Id, driverId.Value);
    }

    public PickupRequest CreateOrGetPickupRequest(Guid passengerId)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot change pickup requests after computation has started.");

        var passenger = _members.FirstOrDefault(m => m.Id == passengerId);
        if (passenger == null)
            throw new DomainException("Passenger not found in the session.");

        if (!passenger.NeedsPickup())
            throw new DomainException("Only members who need pickup can create pickup requests.");

        var existing = _pickupRequests.FirstOrDefault(r => r.PassengerId == passengerId && r.Status != PickupRequestStatus.Cancelled);
        if (existing != null)
            return existing;

        var request = new PickupRequest(Id, passengerId);
        _pickupRequests.Add(request);
        passenger.RemoveDriver();
        return request;
    }

    public void AcceptPickupRequest(Guid pickupRequestId, Guid driverId)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot update pickup assignments after computation has started.");

        var request = _pickupRequests.FirstOrDefault(r => r.Id == pickupRequestId);
        if (request == null)
            throw new DomainException("Pickup request not found.");

        var passenger = _members.FirstOrDefault(m => m.Id == request.PassengerId);
        var driver = _members.FirstOrDefault(m => m.Id == driverId);

        if (passenger == null)
            throw new DomainException("Passenger not found in the session.");

        if (driver == null)
            throw new DomainException("Driver not found in the session.");

        if (!driver.CanOfferPickup())
            throw new DomainException("Selected member cannot offer pickup.");

        if (driver.Id == passenger.Id)
            throw new DomainException("A driver cannot pick up themselves.");

        var activeAssignments = _pickupRequests.Count(r =>
            r.AcceptedDriverId == driverId &&
            r.IsAccepted() &&
            r.PassengerId != passenger.Id);

        if (activeAssignments >= driver.GetSeatCapacity())
            throw new DomainException("Driver capacity has been reached.");

        request.Accept(driverId);
        passenger.SetDriver(driverId);
    }

    public void ReleasePickupRequest(Guid pickupRequestId)
    {
        if (Status != SessionStatus.WaitingForMembers)
            throw new DomainException("Cannot update pickup assignments after computation has started.");

        var request = _pickupRequests.FirstOrDefault(r => r.Id == pickupRequestId);
        if (request == null)
            throw new DomainException("Pickup request not found.");

        request.Release();

        var passenger = _members.FirstOrDefault(m => m.Id == request.PassengerId);
        passenger?.RemoveDriver();
    }

    public bool HasPendingPickupRequests()
    {
        return _pickupRequests.Any(r => r.IsPending());
    }

    public int GetAcceptedPassengerCount(Guid driverId)
    {
        return _pickupRequests.Count(r => r.AcceptedDriverId == driverId && r.IsAccepted());
    }

    public void SetLatestOptimizationSnapshot(string? snapshotJson)
    {
        LatestOptimizationSnapshotJson = snapshotJson;
    }

    public void SetFinalRouteSnapshot(string? snapshotJson)
    {
        FinalRouteSnapshotJson = snapshotJson;
    }

    public void LockDeparture()
    {
        if (Status != SessionStatus.RoutePreview)
            throw new DomainException("Can only lock departure from route preview stage.");

        DepartureLockedAt = DateTime.UtcNow;
        ChangeStatus(SessionStatus.Completed);
    }

    public void ChangeStatus(SessionStatus newStatus)
    {
        if (Status == SessionStatus.Completed)
            throw new DomainException("Cannot change status of a completed session.");

        var valid = (Status, newStatus) switch
        {
            (SessionStatus.WaitingForMembers, SessionStatus.Computing) => true,
            (SessionStatus.Computing, SessionStatus.Voting) => true,
            (SessionStatus.Computing, SessionStatus.Failed) => true,
            (SessionStatus.Voting, SessionStatus.RoutePreview) => true,
            (SessionStatus.Voting, SessionStatus.Failed) => true,
            (SessionStatus.RoutePreview, SessionStatus.Completed) => true,
            (SessionStatus.RoutePreview, SessionStatus.Failed) => true,
            _ => false
        };

        if (!valid)
            throw new DomainException($"Invalid status transition from {Status} to {newStatus}.");

        Status = newStatus;
    }

    public void SubmitVote(Vote vote)
    {
        if (Status != SessionStatus.Voting)
            throw new DomainException("Voting is only allowed during the Voting phase.");

        if (!_members.Any(m => m.Id == vote.MemberId))
            throw new DomainException("Only members of the session can vote.");

        if (!_nominatedVenueIds.Contains(vote.VenueId))
            throw new DomainException("Can only vote for nominated venues.");

        if (_votes.Any(v => v.MemberId == vote.MemberId))
            throw new DomainException("Member has already voted.");

        _votes.Add(vote);
    }

    public void SetNominatedVenues(IEnumerable<string> venueIds)
    {
        _nominatedVenueIds.Clear();
        _nominatedVenueIds.AddRange(venueIds);
    }

    public void SetWinningVenue(string? venueId)
    {
        if (Status != SessionStatus.RoutePreview && Status != SessionStatus.Completed)
            throw new DomainException("Can only set winning venue once voting has finished.");

        WinningVenueId = venueId;
    }

    public bool AllMembersVoted() => _votes.Count == _members.Count && _members.Count > 0;
}
