using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;
using OptiGo.Domain.Exceptions;

namespace OptiGo.Application.UseCases;

internal static class SessionAuthorization
{
    public static Member RequireCurrentMember(Session session, ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated)
            throw new DomainException("Authentication is required.");

        var member = session.Members.FirstOrDefault(m => m.IsOwnedBy(currentUser.Subject));
        if (member == null)
            throw new DomainException("Current user is not a member of this session.");

        return member;
    }

    public static Member RequireHost(Session session, ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated)
            throw new DomainException("Authentication is required.");

        var hostMember = session.Members.OrderBy(m => m.JoinedAt).FirstOrDefault();
        if (hostMember == null || !hostMember.IsOwnedBy(currentUser.Subject))
            throw new DomainException("Only the session host can perform this action.");

        return hostMember;
    }

    public static void RequireMemberOwnerOrHost(Session session, Guid memberId, ICurrentUser currentUser)
    {
        var currentMember = RequireCurrentMember(session, currentUser);
        var hostMember = session.Members.OrderBy(m => m.JoinedAt).FirstOrDefault();
        if (currentMember.Id == memberId || hostMember?.IsOwnedBy(currentUser.Subject) == true)
            return;

        throw new DomainException("Current user cannot act for this member.");
    }
}
