using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId:guid}/chat")]
public class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(Guid sessionId, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var messages = await _mediator.Send(new GetChatMessagesQuery(sessionId, take), ct);
        return Ok(messages);
    }

    [HttpPost]
    [EnableRateLimiting("chat")]
    public async Task<IActionResult> SendMessage(Guid sessionId, [FromBody] SendChatMessageRequest request, CancellationToken ct = default)
    {
        var message = await _mediator.Send(new SendChatMessageCommand(sessionId, request.MemberId, request.Text), ct);
        return Ok(message);
    }
}

public class SendChatMessageRequest
{
    public Guid MemberId { get; set; }
    public string Text { get; set; } = string.Empty;
}
