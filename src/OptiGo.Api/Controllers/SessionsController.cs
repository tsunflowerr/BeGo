using MediatR;
using Microsoft.AspNetCore.Mvc;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetSession(Guid id)
    {
        var session = await _mediator.Send(new GetSessionQuery(id));
        
        if (session == null)
            return NotFound(new { Error = "Session not found", Message = $"Session with ID {id} does not exist." });
        
        return Ok(session);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionCommand command)
    {
        var sessionId = await _mediator.Send(command);
        return Ok(new { SessionId = sessionId });
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> JoinSession(Guid id, [FromBody] JoinSessionRequest request)
    {
        var command = new JoinSessionCommand(
            id, 
            request.MemberName, 
            request.Latitude, 
            request.Longitude, 
            request.TransportMode);

        var memberId = await _mediator.Send(command);
        return Ok(new { MemberId = memberId });
    }

    [HttpPut("{id:guid}/query")]
    public async Task<IActionResult> UpdateQuery(Guid id, [FromBody] UpdateQueryRequest request)
    {
        var command = new UpdateSessionQueryCommand(id, request.QueryText);
        await _mediator.Send(command);
        return Ok(new { Message = "Query updated successfully" });
    }
}

public class JoinSessionRequest
{
    public string MemberName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Domain.Enums.TransportMode TransportMode { get; set; }
}

public class UpdateQueryRequest
{
    public string QueryText { get; set; } = string.Empty;
}
