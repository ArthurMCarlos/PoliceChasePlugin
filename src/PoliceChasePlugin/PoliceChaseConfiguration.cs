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
}
