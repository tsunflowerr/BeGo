using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface IOutingBenchmarkService
{
    Task<OutingBenchmarkReportDto> RunAsync(
        OutingBenchmarkRequestDto request,
        CancellationToken ct = default);
}
