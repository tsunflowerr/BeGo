using OptiGo.Application.Interfaces;
using OptiGo.Application.UseCases;

namespace OptiGo.Infrastructure.Routing;

public class DefaultVenueEvaluator : IVenueEvaluator
{
    public IReadOnlyList<CandidateResultDto> RankCandidates(
        IReadOnlyList<CandidateResultDto> candidates,
        int topN = 3)
    {
        if (candidates.Count == 0)
            return [];

        var minCost = candidates.Min(candidate => candidate.ScoreBreakdown.GeneralizedCostSeconds);
        var maxCost = candidates.Max(candidate => candidate.ScoreBreakdown.GeneralizedCostSeconds);
        var minFairness = candidates.Min(candidate => candidate.ScoreBreakdown.FairnessPenaltySeconds);
        var maxFairness = candidates.Max(candidate => candidate.ScoreBreakdown.FairnessPenaltySeconds);
        var minDetour = candidates.Min(candidate => candidate.MaxDriverDetourSeconds);
        var maxDetour = candidates.Max(candidate => candidate.MaxDriverDetourSeconds);
        var minWalk = candidates.Min(candidate => candidate.TotalWalkingDistanceMeters);
        var maxWalk = candidates.Max(candidate => candidate.TotalWalkingDistanceMeters);

        foreach (var candidate in candidates)
        {
            var normalizedCost = Normalize(candidate.ScoreBreakdown.GeneralizedCostSeconds, minCost, maxCost);
            var normalizedFairness = Normalize(candidate.ScoreBreakdown.FairnessPenaltySeconds, minFairness, maxFairness);
            var normalizedDetour = Normalize(candidate.MaxDriverDetourSeconds, minDetour, maxDetour);
            var normalizedWalk = Normalize(candidate.TotalWalkingDistanceMeters, minWalk, maxWalk);

            candidate.FinalScore = Math.Round(
                100 -
                normalizedCost * 58 -
                normalizedFairness * 16 -
                normalizedDetour * 14 -
                normalizedWalk * 12,
                2);
        }

        return candidates
            .OrderByDescending(candidate => candidate.FinalScore)
            .Take(topN)
            .ToList();
    }

    private static double Normalize(double value, double min, double max)
    {
        if (Math.Abs(max - min) < 0.0001)
            return 0;

        return (value - min) / (max - min);
    }
}
