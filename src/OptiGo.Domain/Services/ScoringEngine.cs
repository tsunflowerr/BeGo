using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

/// <summary>
/// Domain Service thực hiện chấm điểm và chuẩn hóa Min-Max theo yêu cầu của bài toán đa mục tiêu.
/// </summary>
public static class ScoringEngine
{
    /// <summary>
    /// Chấm điểm và chuẩn hóa danh sách các ứng viên.
    /// Giá trị FinalScore trả về: Càng Thấp Càng Tốt (Low is Better).
    /// </summary>
    public static List<CandidateScore> CalculateScores(
        IReadOnlyList<Venue> venues, 
        double[,] travelTimeMatrix, 
        ScoringWeights weights,
        int memberCount)
    {
        if (venues.Count == 0) return new List<CandidateScore>();

        var rawScores = new List<CandidateScore>();

        // 1. Tính toán các chỉ số thô (Raw Metrics)
        for (int j = 0; j < venues.Count; j++)
        {
            var times = new List<double>();
            for (int i = 0; i < memberCount; i++)
            {
                times.Add(travelTimeMatrix[i, j]);
            }

            var totalTime = times.Sum();
            var meanTime = totalTime / times.Count;
            // Tính độ lệch chuẩn (Population Standard Deviation)
            var stdDev = Math.Sqrt(times.Sum(t => Math.Pow(t - meanTime, 2)) / times.Count);

            rawScores.Add(new CandidateScore
            {
                VenueId = venues[j].Id,
                TotalTimeSeconds = totalTime,
                TimeStdDeviation = stdDev,
                QualityRating = venues[j].Rating > 0 ? venues[j].Rating : 3.0 // Default rating
            });
        }

        // 2. Tìm Min/Max để chuẩn bị Min-Max Normalization
        var minTotalTime = rawScores.Min(r => r.TotalTimeSeconds);
        var maxTotalTime = rawScores.Max(r => r.TotalTimeSeconds);
        var minStdDev = rawScores.Min(r => r.TimeStdDeviation);
        var maxStdDev = rawScores.Max(r => r.TimeStdDeviation);
        var minQuality = rawScores.Min(r => r.QualityRating);
        var maxQuality = rawScores.Max(r => r.QualityRating);

        // 3. Min-Max Normalization & Scoring
        foreach (var score in rawScores)
        {
            // Công thức: X_norm = (X - X_min) / (X_max - X_min)
            // Ngăn lỗi chia cho 0 nếu tất cả giá trị đều bằng nhau
            score.NormalizedTotalTime = maxTotalTime == minTotalTime ? 0 
                : (score.TotalTimeSeconds - minTotalTime) / (maxTotalTime - minTotalTime);

            score.NormalizedStdDeviation = maxStdDev == minStdDev ? 0 
                : (score.TimeStdDeviation - minStdDev) / (maxStdDev - minStdDev);

            // Chú ý với Quality: Điểm càng cao càng "tốt", nhưng vì bài toán yêu cầu FinalScore "càng bé càng tốt",
            // Nên Normalize Quality sẽ bị LẬT NGƯỢC (Inverted Min-Max): 
            // Normalized = 0.0 nếu là điểm cao nhất (Tốt nhất), 1.0 nếu là điểm thấp nhất (Tệ nhất)
            score.NormalizedQuality = maxQuality == minQuality ? 0 
                : (maxQuality - score.QualityRating) / (maxQuality - minQuality);

            // 4. Áp dụng trọng số
            score.FinalScore = 
                  weights.Efficiency * score.NormalizedTotalTime
                + weights.Fairness * score.NormalizedStdDeviation
                + weights.Quality * score.NormalizedQuality;
        }

        return rawScores.OrderBy(r => r.FinalScore).ToList();
    }
}
