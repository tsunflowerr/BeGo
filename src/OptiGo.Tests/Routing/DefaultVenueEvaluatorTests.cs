using OptiGo.Application.UseCases;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Tests.Routing;

public class DefaultVenueEvaluatorTests
{
    [Fact]
    public void RankCandidatesKeepsParetoTradeoffsAndLabelsThem()
    {
        var fastest = CreateCandidate("fast", groupSeconds: 600, passengerSeconds: 500, detourSeconds: 300, walkSeconds: 200);
        var fairest = CreateCandidate("fair", groupSeconds: 720, passengerSeconds: 360, detourSeconds: 280, walkSeconds: 220);
        var driverLight = CreateCandidate("driver", groupSeconds: 750, passengerSeconds: 420, detourSeconds: 90, walkSeconds: 240);
        var dominated = CreateCandidate("dominated", groupSeconds: 900, passengerSeconds: 700, detourSeconds: 500, walkSeconds: 500);

        var evaluator = new DefaultVenueEvaluator();

        var ranked = evaluator.RankCandidates([fastest, fairest, driverLight, dominated], 3);

        Assert.DoesNotContain(ranked, candidate => candidate.VenueId == "dominated");
        Assert.Contains(ranked, candidate => candidate.RecommendationType == "Nhanh nhất");
        Assert.Contains(ranked, candidate => candidate.RecommendationType == "Công bằng nhất");
        Assert.Contains(ranked, candidate => candidate.RecommendationType == "Nhẹ cho tài xế");
    }

    [Fact]
    public void RankCandidatesFallsBackToInfeasibleWhenNoFeasibleSolutionExists()
    {
        var infeasible = CreateCandidate("infeasible", groupSeconds: 600, passengerSeconds: 500, detourSeconds: 300, walkSeconds: 200);
        infeasible.IsFeasible = false;
        infeasible.FeasibilityIssues.Add("Driver detour violated.");

        var evaluator = new DefaultVenueEvaluator();

        var ranked = evaluator.RankCandidates([infeasible], 3);

        var only = Assert.Single(ranked);
        Assert.Equal("infeasible", only.VenueId);
    }

    private static CandidateResultDto CreateCandidate(
        string id,
        double groupSeconds,
        double passengerSeconds,
        double detourSeconds,
        double walkSeconds) =>
        new()
        {
            VenueId = id,
            Name = id,
            Category = "cafe",
            Rating = 4.5,
            IsFeasible = true,
            MaxDriverDetourSeconds = detourSeconds,
            TotalWalkingDistanceMeters = walkSeconds,
            Metrics = new SolutionMetricsDto
            {
                TotalGroupTimeSeconds = groupSeconds,
                MaxPassengerTimeSeconds = passengerSeconds,
                StdPassengerTimeSeconds = passengerSeconds / 10,
                MaxDriverDetourSeconds = detourSeconds,
                TotalDriverDetourSeconds = detourSeconds,
                TotalWalkingTimeSeconds = walkSeconds,
                MaxWalkingTimeSeconds = walkSeconds
            },
            ScoreBreakdown = new RouteScoreBreakdownDto
            {
                GeneralizedCostSeconds = groupSeconds + passengerSeconds + detourSeconds + walkSeconds
            }
        };
}
