using MediatR;
using Microsoft.AspNetCore.Mvc;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OptimizerController : ControllerBase
{
    private readonly IMediator _mediator;

    public OptimizerController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("session/{id:guid}/optimize")]
    public async Task<IActionResult> FindMeetPoint(Guid id, [FromQuery] string category = "cafe")
    {
        var command = new FindMeetPointCommand(id, category);
        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return BadRequest(new { Error = result.ErrorMessage });
        }

        return Ok(result);
    }
}
