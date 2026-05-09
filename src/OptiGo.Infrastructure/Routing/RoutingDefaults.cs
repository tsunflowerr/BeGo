namespace OptiGo.Infrastructure.Routing;

internal static class RoutingDefaults
{
    public const double MaxWalkDistanceMeters = 500;
    public const double MaxWalkMinutes = 8;
    public const double MaxWalkSeconds = MaxWalkMinutes * 60;
    public const double SharedStopTargetWalkSeconds = 5 * 60;
    public const double MaxPassengerTotalTravelSeconds = 35 * 60;
    public const double MaxDriverDetourSeconds = 15 * 60;
    public const double WalkSpeedMetersPerSecond = 1.25;
    public const double SyncBufferSeconds = 45;
    public const double RoadsideAccessPenaltySeconds = 20;
    public const double SharedStopAccessPenaltySeconds = 4;
    public const double ApproximateRoadsideRiskSeconds = 25;
    public const double SharedStopRiskSeconds = 4;
    public const double WaitEtaFactor = 0.05;
    public const double WalkWeight = 1.25;
    public const double WaitWeight = 1.1;
    public const double DetourWeight = 0.7;
    public const double FairnessWeight = 0.75;
    public const double StopComplexityWeight = 18;
    public const double StabilityWeight = 0.15;
    public const double QualityBonusCapSeconds = 240;
    public const double BasePickupServiceSeconds = 60;
    public const double BoardingServiceSecondsPerPassenger = 30;
    public const int MaxStopsPerPassenger = 8;
    public const int MaxSharedStopsPerCluster = 5;
    public const int MaxDriverCorridorStops = 8;
    public const int MaxVenueCandidates = 15;
    public const int SmallGroupExactMemberLimit = 10;
    public const int MaxAssignmentSolutions = 64;
    public const int MaxExactAssignmentStates = 200_000;
    public const int MaxRoutePoolCandidatesPerDriver = 50;
    public const int MaxExactRoutePoolSubsetsPerDriver = 1_024;
    public const int MaxRoutePoolSolutions = 64;
    public const int MaxRefinementIterations = 200;
    public const int ExactRouteStopLimit = 9;
    public const double FeasibilityIssuePenaltySeconds = 600;
    public const double StopDeduplicationMeters = 20;
    public const double SharedClusterRadiusMeters = 500;
    public const double DriverCorridorMeters = 300;
    public const double ArrivalSpreadSoftLimitSeconds = 10 * 60;
}
