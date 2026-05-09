using MediatR;
using OptiGo.Application.Interfaces;

namespace OptiGo.Application.UseCases;

public record RunOutingBenchmarkQuery(
    int Seed = 20260505,
    int ScenarioCount = 18) : IRequest<OutingBenchmarkReportDto>;

public class RunOutingBenchmarkHandler : IRequestHandler<RunOutingBenchmarkQuery, OutingBenchmarkReportDto>
{
    private readonly IOutingBenchmarkService _benchmarkService;

    public RunOutingBenchmarkHandler(IOutingBenchmarkService benchmarkService)
    {
        _benchmarkService = benchmarkService;
    }

    public async Task<OutingBenchmarkReportDto> Handle(
        RunOutingBenchmarkQuery request,
        CancellationToken cancellationToken) =>
        await _benchmarkService.RunAsync(
            new OutingBenchmarkRequestDto
            {
                Seed = request.Seed,
                ScenarioCount = request.ScenarioCount
            },
            cancellationToken);
}

public class OutingBenchmarkRequestDto
{
    public int Seed { get; init; } = 20260505;
    public int ScenarioCount { get; init; } = 18;
}

public class OutingBenchmarkReportDto
{
    public int Seed { get; init; }
    public int ScenarioCount { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public double TotalRuntimeMs { get; init; }
    public List<BenchmarkSourceDto> Sources { get; init; } = new();
    public List<BenchmarkAlgorithmAggregateDto> Aggregates { get; init; } = new();
    public List<BenchmarkScenarioResultDto> Scenarios { get; init; } = new();
    public List<BenchmarkWeaknessDto> Weaknesses { get; init; } = new();
}

public class BenchmarkSourceDto
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Relevance { get; init; } = string.Empty;
}

public class BenchmarkAlgorithmAggregateDto
{
    public string AlgorithmKey { get; init; } = string.Empty;
    public string AlgorithmName { get; init; } = string.Empty;
    public bool IsOptiGo { get; init; }
    public int Runs { get; init; }
    public double FeasibleRate { get; init; }
    public double WinRate { get; init; }
    public double AverageObjectiveSeconds { get; init; }
    public double AverageTotalGroupTimeSeconds { get; init; }
    public double AverageMaxPassengerTimeSeconds { get; init; }
    public double AverageMaxMemberBurdenSeconds { get; init; }
    public double AverageWorstMemberRegretSeconds { get; init; }
    public double AveragePassengerBurdenGini { get; init; }
    public double AverageMaxDriverDetourSeconds { get; init; }
    public double AverageStdDriverDetourSeconds { get; init; }
    public double AverageDriverDetourGini { get; init; }
    public double AverageMaxWalkingTimeSeconds { get; init; }
    public double AverageSharedStopRate { get; init; }
    public double AverageStopCount { get; init; }
    public double AverageComputeTimeMs { get; init; }
}

public class BenchmarkScenarioResultDto
{
    public string ScenarioId { get; init; } = string.Empty;
    public string Layout { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public int DriverCount { get; init; }
    public int PickupPassengerCount { get; init; }
    public int VenueCount { get; init; }
    public string Description { get; init; } = string.Empty;
    public List<BenchmarkMemberDto> Members { get; init; } = new();
    public List<BenchmarkVenueDto> Venues { get; init; } = new();
    public List<BenchmarkAlgorithmRunDto> Runs { get; init; } = new();
}

public class BenchmarkMemberDto
{
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string TransportMode { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int SeatCapacity { get; init; }
}

public class BenchmarkVenueDto
{
    public string VenueId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Rating { get; init; }
}

public class BenchmarkAlgorithmRunDto
{
    public string ScenarioId { get; set; } = string.Empty;
    public string AlgorithmKey { get; init; } = string.Empty;
    public string AlgorithmName { get; init; } = string.Empty;
    public bool IsOptiGo { get; init; }
    public string SelectedVenueId { get; init; } = string.Empty;
    public string SelectedVenueName { get; init; } = string.Empty;
    public bool IsFeasible { get; init; }
    public List<string> FeasibilityIssues { get; init; } = new();
    public double ObjectiveSeconds { get; init; }
    public double TotalGroupTimeSeconds { get; init; }
    public double MaxPassengerTimeSeconds { get; init; }
    public double StdPassengerTimeSeconds { get; init; }
    public double MaxMemberBurdenSeconds { get; init; }
    public double WorstMemberRegretSeconds { get; init; }
    public double PassengerBurdenGini { get; init; }
    public double MaxDriverDetourSeconds { get; init; }
    public double StdDriverDetourSeconds { get; init; }
    public double DriverDetourGini { get; init; }
    public double TotalDriverDetourSeconds { get; init; }
    public double MaxWalkingTimeSeconds { get; init; }
    public double TotalWalkingTimeSeconds { get; init; }
    public double SharedStopRate { get; init; }
    public int StopCount { get; init; }
    public int SharedStopCount { get; init; }
    public double ComputeTimeMs { get; init; }
    public double GapToBestExternalPercent { get; set; }
}

public class BenchmarkWeaknessDto
{
    public string ScenarioId { get; init; } = string.Empty;
    public string Layout { get; init; } = string.Empty;
    public string Metric { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double OptiGoValue { get; init; }
    public double BestExternalValue { get; init; }
    public string BestExternalAlgorithm { get; init; } = string.Empty;
    public double GapPercent { get; init; }
}
