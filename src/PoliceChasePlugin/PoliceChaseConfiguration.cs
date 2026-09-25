using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class PoliceChaseConfiguration : IValidateConfiguration<PoliceChaseConfigurationValidator>
{
    public bool Enabled { get; set; } = true;
    public string PoliceCarModel { get; set; } = "";
    public int PoliceCarSessionId { get; set; } = -1;
    public float PursuitMaxDistanceMeters { get; set; } = 1500;
    public float PursuitDesiredDistanceMeters { get; set; } = 50;
    public float PursuitMaxSpeedKph { get; set; } = 180;
    public int PursuitUpdateIntervalMilliseconds { get; set; } = 200;
    public float PursuitRouteSearchMaxDistanceMeters { get; set; } = 20_000;
    public int PursuitRouteSearchMaxVisitedNodes { get; set; } = 50_000;
    public int PursuitRouteGraceMilliseconds { get; set; } = 2_000;
    public int PursuitNoRouteProbeIntervalMilliseconds { get; set; } = 2_000;
    public bool PursuitLaneChangeEnabled { get; set; } = true;
    public float PursuitLaneChangeDistanceMeters { get; set; } = 60;
    public int PursuitLaneChangeCooldownMilliseconds { get; set; } = 3_000;
    public float PursuitLaneChangeLookaheadMeters { get; set; } = 1_000;
    public bool PursuitAggressiveDrivingEnabled { get; set; }
    public bool PursuitContactEnabled { get; set; } = true;
    public float PursuitCatchUpDistanceMeters { get; set; } = 100;
    public float PursuitCloseDistanceMeters { get; set; } = 15;
    public float PursuitContactDistanceMeters { get; set; } = 3;
    public float PursuitMaxClosingSpeedKph { get; set; } = 35;
    public float PursuitContactClosingSpeedKph { get; set; } = 5;
    public bool PursuitPitEnabled { get; set; }
    public float PursuitPitMaxDistanceMeters { get; set; } = 6;
    public float PursuitPitMaxClosingSpeedKph { get; set; } = 10;
    public float PursuitPitLateralOffsetMeters { get; set; } = 0.8f;
    public int PursuitPitCommitMilliseconds { get; set; } = 1200;
    public int PursuitPitCooldownMilliseconds { get; set; } = 3000;
    public bool PursuitCloseEnabled { get; set; }
    public float PursuitCloseAssistStartMeters { get; set; } = 60;
    public float PursuitCloseAssistFullMeters { get; set; } = 250;
    public float PursuitCloseMaxAdvantageKph { get; set; } = 80;
    public float PursuitCloseMaxAccelerationMetersPerSecondSquared { get; set; } = 10;
    public float PursuitCloseMaxJerkMetersPerSecondCubed { get; set; } = 5;
    public float PursuitCloseMaxSpeedKph { get; set; } = 400;
    public int PursuitCloseObstacleHoldMilliseconds { get; set; } = 1000;
    public float PursuitCloseObstacleDeficitKph { get; set; } = 10;
    public float PursuitCloseBypassLookaheadMeters { get; set; } = 150;
    public float PursuitCloseReturnClearanceMeters { get; set; } = 12;
    public float PursuitCloseEscapeDistanceMeters { get; set; } = 800;
    public int PursuitCloseEscapeHoldMilliseconds { get; set; } = 15000;
    public int PursuitCloseRearmDelayMilliseconds { get; set; } = 60000;

    internal Ai.PolicePursuitCloseOptions? CreateClosePursuitOptions() =>
        !PursuitCloseEnabled ? null : new(
            PursuitCloseAssistStartMeters,
            PursuitCloseAssistFullMeters,
            PursuitCloseMaxAdvantageKph / 3.6f,
            PursuitCloseMaxAccelerationMetersPerSecondSquared,
            PursuitCloseMaxJerkMetersPerSecondCubed,
            PursuitCloseMaxSpeedKph / 3.6f,
            PursuitCloseObstacleHoldMilliseconds,
            PursuitCloseObstacleDeficitKph / 3.6f,
            PursuitCloseBypassLookaheadMeters,
            PursuitCloseReturnClearanceMeters,
            PursuitCloseEscapeDistanceMeters,
            PursuitCloseEscapeHoldMilliseconds,
            PursuitCloseRearmDelayMilliseconds);
}
