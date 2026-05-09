using MediatR;
using Microsoft.AspNetCore.Mvc;
using OptiGo.Application.UseCases;

namespace OptiGo.Api.Controllers;

[ApiController]
[Route("api/benchmarks")]
public class BenchmarksController : ControllerBase
{
    private readonly IMediator _mediator;

    public BenchmarksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("outing")]
    public async Task<IActionResult> RunOutingBenchmark(
        [FromQuery] int seed = 20260505,
        [FromQuery] int scenarioCount = 18,
        CancellationToken ct = default)
    {
        var boundedScenarioCount = Math.Clamp(scenarioCount, 1, 60);
        var report = await _mediator.Send(
            new RunOutingBenchmarkQuery(seed, boundedScenarioCount),
            ct);

        return Ok(report);
    }
}
