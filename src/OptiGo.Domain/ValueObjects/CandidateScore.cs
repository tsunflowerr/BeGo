using System;

namespace OptiGo.Domain.ValueObjects;

public struct ScoringWeights
{
    public double Efficiency { get; }
    public double Fairness { get; }
    public double Quality { get; }

    public static ScoringWeights Default => new ScoringWeights(0.6, 0.3, 0.1);

    public ScoringWeights(double efficiency, double fairness, double quality)
    {
        Efficiency = efficiency;
        Fairness = fairness;
        Quality = quality;

        if (Math.Abs((Efficiency + Fairness + Quality) - 1.0) > 1e-4)
            throw new ArgumentException("Sum of weights must be exactly 1.0");
    }
}

public class CandidateScore
{
    public string VenueId { get; init; } = null!;
    
    // Raw Metrics
    public double TotalTimeSeconds { get; init; }
    public double TimeStdDeviation { get; init; }
    public double QualityRating { get; init; }

    // Normalized Metrics [0.0 - 1.0]
    public double NormalizedTotalTime { get; set; }
    public double NormalizedStdDeviation { get; set; }
    public double NormalizedQuality { get; set; }

    // Final Computed Score (Smaller is better, because we want MINIMUM time and variation)
    public double FinalScore { get; set; }
}
