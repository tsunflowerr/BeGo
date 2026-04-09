using MediatR;
using System;

namespace OptiGo.Application.UseCases;

public record SubmitVoteCommand(Guid SessionId, Guid MemberId, string VenueId) : IRequest<SubmitVoteResult>;

public class SubmitVoteResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsVotingCompleted { get; set; }

    public string? WinningVenueId { get; set; }
}
