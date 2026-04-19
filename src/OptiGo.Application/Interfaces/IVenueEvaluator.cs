using OptiGo.Application.UseCases;

namespace OptiGo.Application.Interfaces;

public interface IVenueEvaluator
{
    IReadOnlyList<CandidateResultDto> RankCandidates(
        IReadOnlyList<CandidateResultDto> candidates,
        int topN = 3);
}
