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
}
