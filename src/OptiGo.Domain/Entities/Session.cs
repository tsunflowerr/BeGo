using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Enums;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Domain.Entities;

/// <summary>
/// Session Aggregate Root — Quản lý toàn bộ lifecycle của 1 phiên tìm điểm hẹn.
/// EF Core sẽ sử dụng private constructor để hydrate từ DB.
/// </summary>
public class Session
{
    public Guid Id { get; private set; }
    public string HostName { get; private set; } = null!;
    public SessionStatus Status { get; private set; }
    public string? QueryText { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    // EF Core sẽ access backing field "_members" thông qua convention
    private readonly List<Member> _members = new();
    public IReadOnlyCollection<Member> Members => _members.AsReadOnly();

    private readonly List<Vote> _votes = new();
    public IReadOnlyCollection<Vote> Votes => _votes.AsReadOnly();

    // EF Core cần private parameterless constructor
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
            _members.Remove(member);
        }
    }

    public void ChangeStatus(SessionStatus newStatus)
    {
        if (Status == SessionStatus.Completed)
            throw new DomainException("Cannot change status of a completed session.");

        // Enforce valid transitions
        var valid = (Status, newStatus) switch
        {
            (SessionStatus.WaitingForMembers, SessionStatus.Computing) => true,
            (SessionStatus.Computing, SessionStatus.Voting) => true,
            (SessionStatus.Computing, SessionStatus.Failed) => true,
            (SessionStatus.Voting, SessionStatus.Completed) => true,
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

        if (_votes.Any(v => v.MemberId == vote.MemberId))
            throw new DomainException("Member has already voted.");

        _votes.Add(vote);
    }

    public bool AllMembersVoted() => _votes.Count == _members.Count && _members.Count > 0;
}
