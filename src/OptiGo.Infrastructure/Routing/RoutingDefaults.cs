namespace OptiGo.Infrastructure.Routing;

internal static class RoutingDefaults
{
    public const double MaxWalkDistanceMeters = 400;
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
}
