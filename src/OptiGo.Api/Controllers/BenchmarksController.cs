using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> RunOutingBenchmark(
        [FromQuery] int seed = 20260505,
        [FromQuery] int scenarioCount = 18,
        [FromQuery] string benchmarkMode = "synthetic",
        [FromQuery] string? publicDataRoot = null,
        [FromQuery] int darpSlicesPerFile = 8,
        [FromQuery] int liLimSlicesPerFile = 8,
        [FromQuery] int publicMaxVenuesPerScenario = 4,
        CancellationToken ct = default)
    {
        var boundedScenarioCount = Math.Clamp(scenarioCount, 1, 180);
        var report = await _mediator.Send(
            new RunOutingBenchmarkQuery(
                seed,
                boundedScenarioCount,
                benchmarkMode,
                publicDataRoot,
                darpSlicesPerFile,
                liLimSlicesPerFile,
                publicMaxVenuesPerScenario),
            ct);

        return Ok(report);
    }
}
