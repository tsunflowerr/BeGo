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

        var compositeCosts = candidates.ToDictionary(
            candidate => candidate.VenueId,
            RoutingSolutionScorer.CalculateCompositeCost,
            StringComparer.Ordinal);
        var minCost = compositeCosts.Values.Min();
        var maxCost = compositeCosts.Values.Max();
        var minFairness = candidates.Min(candidate => candidate.ScoreBreakdown.FairnessPenaltySeconds);
        var maxFairness = candidates.Max(candidate => candidate.ScoreBreakdown.FairnessPenaltySeconds);
        var minDetour = candidates.Min(candidate => candidate.MaxDriverDetourSeconds);
        var maxDetour = candidates.Max(candidate => candidate.MaxDriverDetourSeconds);
        var minWalk = candidates.Min(candidate => candidate.TotalWalkingDistanceMeters);
        var maxWalk = candidates.Max(candidate => candidate.TotalWalkingDistanceMeters);

        foreach (var candidate in candidates)
        {
            var normalizedCost = Normalize(compositeCosts[candidate.VenueId], minCost, maxCost);
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

        var feasibleCandidates = candidates.Where(candidate => candidate.IsFeasible).ToList();
        var selectionPool = feasibleCandidates.Count > 0 ? feasibleCandidates : candidates.ToList();
        var paretoFront = BuildParetoFront(selectionPool);
        var selected = SelectLabeledCandidates(paretoFront, topN);

        foreach (var candidate in selectionPool
                     .OrderByDescending(candidate => candidate.FinalScore))
        {
            if (selected.Count >= topN)
                break;

            if (selected.All(existing => existing.VenueId != candidate.VenueId))
            {
                selected.Add(candidate);
            }
        }

        return selected
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

    private static List<CandidateResultDto> BuildParetoFront(IReadOnlyList<CandidateResultDto> candidates) =>
        candidates
            .Where(candidate => !candidates.Any(other => other.VenueId != candidate.VenueId && Dominates(other, candidate)))
            .OrderByDescending(candidate => candidate.FinalScore)
            .ToList();

    private static bool Dominates(CandidateResultDto a, CandidateResultDto b)
    {
        var notWorse =
            a.Metrics.TotalGroupTimeSeconds <= b.Metrics.TotalGroupTimeSeconds &&
            a.Metrics.MaxPassengerTimeSeconds <= b.Metrics.MaxPassengerTimeSeconds &&
            a.Metrics.MaxDriverDetourSeconds <= b.Metrics.MaxDriverDetourSeconds &&
            a.Metrics.TotalWalkingTimeSeconds <= b.Metrics.TotalWalkingTimeSeconds;
        var strictlyBetter =
            a.Metrics.TotalGroupTimeSeconds < b.Metrics.TotalGroupTimeSeconds ||
            a.Metrics.MaxPassengerTimeSeconds < b.Metrics.MaxPassengerTimeSeconds ||
            a.Metrics.MaxDriverDetourSeconds < b.Metrics.MaxDriverDetourSeconds ||
            a.Metrics.TotalWalkingTimeSeconds < b.Metrics.TotalWalkingTimeSeconds;

        return notWorse && strictlyBetter;
    }

    private static List<CandidateResultDto> SelectLabeledCandidates(
        IReadOnlyList<CandidateResultDto> candidates,
        int topN)
    {
        var selected = new List<CandidateResultDto>();
        AddLabeledCandidate(
            selected,
            candidates.OrderBy(candidate => candidate.Metrics.TotalGroupTimeSeconds).FirstOrDefault(),
            "Nhanh nhất",
            "Tổng thời gian nhóm thấp nhất trong các phương án không bị dominate.");
        AddLabeledCandidate(
            selected,
            candidates.OrderBy(candidate => candidate.Metrics.MaxPassengerTimeSeconds)
                .ThenBy(candidate => candidate.Metrics.StdPassengerTimeSeconds)
                .FirstOrDefault(),
            "Công bằng nhất",
            "Giảm thời gian passenger lâu nhất và độ lệch giữa các passenger.");
        AddLabeledCandidate(
            selected,
            candidates.OrderBy(candidate => candidate.Metrics.MaxDriverDetourSeconds)
                .ThenBy(candidate => candidate.Metrics.TotalDriverDetourSeconds)
                .FirstOrDefault(),
            "Nhẹ cho tài xế",
            "Giữ detour của tài xế thấp nhất.");

        return selected.Take(topN).ToList();
    }

    private static void AddLabeledCandidate(
        ICollection<CandidateResultDto> selected,
        CandidateResultDto? candidate,
        string label,
        string reason)
    {
        if (candidate == null || selected.Any(existing => existing.VenueId == candidate.VenueId))
            return;

        candidate.RecommendationType = label;
        candidate.OptimizationReason = reason + " " + candidate.OptimizationReason;
        selected.Add(candidate);
    }
}
