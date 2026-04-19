using OptiGo.Application.UseCases;
using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IBaselineOutingRoutePlanner
{
    Task<CandidateResultDto> PlanVenueAsync(
        Session session,
        Venue venue,
        CancellationToken ct = default);
}
