using OptiGo.Domain.Entities;

namespace OptiGo.Application.Interfaces;

public interface IVenuePrefilter
{
    Task<IReadOnlyList<Venue>> FilterTopCandidatesAsync(
        Session session,
        IReadOnlyList<Venue> rawVenues,
        int topN = 15,
        CancellationToken ct = default);
}
