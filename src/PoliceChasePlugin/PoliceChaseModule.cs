using AssettoServer.Server.Plugin;
using Autofac;
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Pursuit;

namespace PoliceChasePlugin;

public sealed class PoliceChaseModule : AssettoServerModule<PoliceChaseConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AssettoServerPlayerSource>()
            .As<IPolicePlayerSource>()
            .SingleInstance();
        builder.RegisterType<PoliceTargetService>().SingleInstance();
        builder.RegisterType<AssettoServerPoliceAiSlotSource>()
            .As<IPoliceAiSlotSource>()
            .SingleInstance();
        builder.RegisterType<PoliceAiService>().SingleInstance();
        builder.RegisterType<PolicePursuitService>()
            .AsSelf()
            .As<IPolicePursuitService>()
            .SingleInstance();
        builder.RegisterType<PoliceChaseService>()
            .AsSelf()
            .As<IAssettoServerAutostart>()
            .SingleInstance();
    }
}
