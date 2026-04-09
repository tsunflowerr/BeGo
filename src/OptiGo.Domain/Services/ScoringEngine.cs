using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

public static class ScoringEngine
{

    public static List<CandidateScore> CalculateScores(
        IReadOnlyList<Venue> venues,
        double[,] travelTimeMatrix,
        ScoringWeights weights,
        int memberCount)
    {
        if (venues.Count == 0) return new List<CandidateScore>();

        var rawScores = new List<CandidateScore>();

        for (int j = 0; j < venues.Count; j++)
        {
            var times = new List<double>();
            for (int i = 0; i < memberCount; i++)
            {
                times.Add(travelTimeMatrix[i, j]);
            }

            var totalTime = times.Sum();
            var meanTime = totalTime / times.Count;

            var stdDev = Math.Sqrt(times.Sum(t => Math.Pow(t - meanTime, 2)) / times.Count);

            rawScores.Add(new CandidateScore
            {
                VenueId = venues[j].Id,
                TotalTimeSeconds = totalTime,
                TimeStdDeviation = stdDev,
                QualityRating = venues[j].Rating > 0 ? venues[j].Rating : 3.0
            });
        }

        var minTotalTime = rawScores.Min(r => r.TotalTimeSeconds);
        var maxTotalTime = rawScores.Max(r => r.TotalTimeSeconds);
        var minStdDev = rawScores.Min(r => r.TimeStdDeviation);
        var maxStdDev = rawScores.Max(r => r.TimeStdDeviation);
        var minQuality = rawScores.Min(r => r.QualityRating);
        var maxQuality = rawScores.Max(r => r.QualityRating);

        foreach (var score in rawScores)
        {

            score.NormalizedTotalTime = maxTotalTime == minTotalTime ? 0
                : (score.TotalTimeSeconds - minTotalTime) / (maxTotalTime - minTotalTime);

            score.NormalizedStdDeviation = maxStdDev == minStdDev ? 0
                : (score.TimeStdDeviation - minStdDev) / (maxStdDev - minStdDev);

            score.NormalizedQuality = maxQuality == minQuality ? 0
                : (maxQuality - score.QualityRating) / (maxQuality - minQuality);

            score.FinalScore =
                  weights.Efficiency * score.NormalizedTotalTime
                + weights.Fairness * score.NormalizedStdDeviation
                + weights.Quality * score.NormalizedQuality;
        }

        return rawScores.OrderBy(r => r.FinalScore).ToList();
    }
}
