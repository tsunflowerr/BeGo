using Microsoft.AspNetCore.Mvc;
using MediatR;
using OptiGo.Application.UseCases;
using System;
using System.Threading.Tasks;

namespace OptiGo.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VoteController : ControllerBase
    {
        private readonly IMediator _mediator;

        public VoteController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost("{sessionId}")]
        public async Task<IActionResult> Vote(Guid sessionId, [FromBody] SubmitVoteRequest request)
        {
            var command = new SubmitVoteCommand(sessionId, request.MemberId, request.VenueId);
            var result = await _mediator.Send(command);

            if (!result.IsSuccess)
                return BadRequest(new { error = result.ErrorMessage });

            return Ok(new 
            { 
                message = "Vote submitted successfully", 
                isVotingCompleted = result.IsVotingCompleted,
                winningVenueId = result.WinningVenueId
            });
        }
    }

    public class SubmitVoteRequest
    {
        public Guid MemberId { get; set; }
        public string VenueId { get; set; } = null!;
    }
}
