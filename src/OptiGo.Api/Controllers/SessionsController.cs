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
        var result = await _mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> JoinSession(Guid id, [FromBody] JoinSessionRequest request)
    {
        var command = new JoinSessionCommand(
            id,
            request.MemberName,
            request.Latitude,
            request.Longitude,
            request.TransportMode,
            request.MobilityRole);

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

    [HttpPut("{id:guid}/members/{memberId:guid}/driver")]
    public async Task<IActionResult> UpdateMemberDriver(Guid id, Guid memberId, [FromBody] UpdateMemberDriverRequest request)
    {
        var command = new UpdateMemberDriverCommand(id, memberId, request.DriverId);
        await _mediator.Send(command);

        return Ok(new
        {
            Message = request.DriverId.HasValue
                ? "Pickup assignment updated successfully"
                : "Pickup assignment removed successfully"
        });
    }

    [HttpPost("{id:guid}/pickup-requests/{requestId:guid}/accept")]
    public async Task<IActionResult> AcceptPickupRequest(Guid id, Guid requestId, [FromBody] AcceptPickupRequest request)
    {
        await _mediator.Send(new AcceptPickupRequestCommand(id, requestId, request.DriverId));
        return Ok(new { Message = "Pickup request accepted successfully" });
    }

    [HttpPost("{id:guid}/pickup-requests/{requestId:guid}/release")]
    public async Task<IActionResult> ReleasePickupRequest(Guid id, Guid requestId)
    {
        await _mediator.Send(new ReleasePickupRequestCommand(id, requestId));
        return Ok(new { Message = "Pickup request released successfully" });
    }

    [HttpPost("{id:guid}/departure/lock")]
    public async Task<IActionResult> LockDeparture(Guid id)
    {
        await _mediator.Send(new LockDepartureCommand(id));
        return Ok(new { Message = "Departure locked successfully" });
    }
}

public class JoinSessionRequest
{
    public string MemberName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Domain.Enums.TransportMode TransportMode { get; set; }
    public Domain.Enums.MemberMobilityRole MobilityRole { get; set; } = Domain.Enums.MemberMobilityRole.SelfTravel;
}

public class UpdateQueryRequest
{
    public string QueryText { get; set; } = string.Empty;
}

public class UpdateMemberDriverRequest
{
    public Guid? DriverId { get; set; }
}

public class AcceptPickupRequest
{
    public Guid DriverId { get; set; }
}
