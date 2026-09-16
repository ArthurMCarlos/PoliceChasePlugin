using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class PoliceChaseConfiguration : IValidateConfiguration<PoliceChaseConfigurationValidator>
{
    public bool Enabled { get; set; } = true;
    public string PoliceCarModel { get; set; } = "";
    public int PoliceCarSessionId { get; set; } = -1;
    public float MaxPoliceSpeedKph { get; set; } = 280;
    public float FarDistanceMeters { get; set; } = 800;
    public float MediumDistanceMeters { get; set; } = 400;
    public float NearDistanceMeters { get; set; } = 150;
    public float FarSpeedBonusKph { get; set; } = 60;
    public float MediumSpeedBonusKph { get; set; } = 40;
    public float NearSpeedBonusKph { get; set; } = 20;
    public float LostDistanceMeters { get; set; } = 2000;
    public bool Debug { get; set; } = true;
    public int DebugIntervalMs { get; set; } = 1000;
}
