using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.ValueObjects;

namespace OptiGo.Domain.Services;

/// <summary>
/// Tính toán tâm hình học (Geometric Median) sử dụng thuật toán Weiszfeld.
/// Giảm thiểu tổng khoảng cách (Σd) thay vì bình phương khoảng cách như Centroid.
/// </summary>
public static class GeometricMedianCalculator
{
    private const double Epsilon = 1e-6; // Ngưỡng hội tụ (khoảng 11cm sai số)
    private const int MaxIterations = 100;

    public static Coordinate Calculate(IReadOnlyList<Coordinate> origins)
    {
        if (origins == null || !origins.Any())
            throw new ArgumentException("Origins cannot be null or empty.");

        if (origins.Count == 1)
            return origins[0];

        // Khởi tạo điểm đầu (seed) bằng Centroid
        var currentPoint = CalculateCentroid(origins);

        for (int i = 0; i < MaxIterations; i++)
        {
            double numeratorLat = 0;
            double numeratorLon = 0;
            double denominator = 0;

            foreach (var origin in origins)
            {
                // Dùng Haversine để tính khoảng cách thực tế thay vì Euclidean
                var distance = currentPoint.DistanceTo(origin);
                
                // Tránh chia cho 0 nếu điểm hiện tại vô tình trùng với 1 origin
                var weight = distance == 0 ? 1 / Epsilon : 1 / distance;

                numeratorLat += origin.Latitude * weight;
                numeratorLon += origin.Longitude * weight;
                denominator += weight;
            }

            var nextLat = numeratorLat / denominator;
            var nextLon = numeratorLon / denominator;
            var nextPoint = new Coordinate(nextLat, nextLon);

            // Kiểm tra điều kiện hội tụ
            if (currentPoint.DistanceTo(nextPoint) < Epsilon)
            {
                return nextPoint;
            }

            currentPoint = nextPoint;
        }

        // Trả về điểm hội tụ tốt nhất đạt được sau MaxIterations
        return currentPoint;
    }

    private static Coordinate CalculateCentroid(IReadOnlyList<Coordinate> origins)
    {
        var avgLat = origins.Average(o => o.Latitude);
        var avgLon = origins.Average(o => o.Longitude);
        return new Coordinate(avgLat, avgLon);
    }
}
