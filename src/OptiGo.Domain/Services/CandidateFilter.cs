using System;
using System.Collections.Generic;
using System.Linq;
using OptiGo.Domain.Entities;

namespace OptiGo.Domain.Services;

/// <summary>
/// Domain Service: Lọc sơ bộ danh sách Venues bằng thuật toán Haversine đường chim bay.
/// Mục đích: Tránh đẩy hàng trăm venues vào OSRM/Mapbox Matrix API gây tốn kém/chậm.
/// Chỉ giữ lại Top N (thường là 15-20) quán có tổng thời gian bay ước tính tốt nhất.
/// </summary>
public static class CandidateFilter
{
    private const double AverageSpeedMetersPerSecond = 11.0; // Khoảng 40 km/h đường nội thành

    public static IReadOnlyList<Venue> FilterTopCandidates(
        IReadOnlyList<Member> members, 
        IReadOnlyList<Venue> rawVenues, 
        int topN = 25)
    {
        if (rawVenues.Count <= topN)
            return rawVenues;

        // Tuple (Venue, EstimateMaxTimeOrSumTime)
        var venueEstimates = new List<(Venue Venue, double EstimateScore)>();

        foreach (var venue in rawVenues)
        {
            var estimatedTimes = new List<double>();
            foreach (var member in members)
            {
                var distanceMeters = member.GetLocation().DistanceTo(venue.GetLocation());
                var estimatedSeconds = distanceMeters / AverageSpeedMetersPerSecond;

                // Simple scaling base on transport mode to somewhat reflect reality
                var transportFactor = GetSimpleTransportFactor(member.TransportMode);
                estimatedTimes.Add(estimatedSeconds * transportFactor);
            }

            // Dùng Max Time (Ưu tiên người đi xa nhất) * Tổng thời gian (Efficiency) 
            // để đảm bảo không cắt nhầm quán nằm ở giữa
            var score = estimatedTimes.Max() + estimatedTimes.Sum();
            
            venueEstimates.Add((venue, score));
        }

        // Ưu tiên Score THẤP nhất (Nhanh nhất)
        return venueEstimates
            .OrderBy(v => v.EstimateScore)
            .Take(topN)
            .Select(v => v.Venue)
            .ToList();
    }

    private static double GetSimpleTransportFactor(Enums.TransportMode mode) => mode switch
    {
        Enums.TransportMode.Walking => 4.0,   // ~10km/h (chậm gấp 4)
        Enums.TransportMode.Cycling => 2.0,   // ~20km/h (chậm gấp 2)
        Enums.TransportMode.Motorbike => 0.85,
        Enums.TransportMode.Car => 1.0,
        Enums.TransportMode.Bus => 1.8,
        _ => 1.0
    };
}
