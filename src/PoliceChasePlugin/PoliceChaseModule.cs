using AssettoServer.Server.Plugin;
using Autofac;

namespace PoliceChasePlugin;

public sealed class PoliceChaseModule : AssettoServerModule<PoliceChaseConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<PoliceChaseService>()
            .AsSelf()
            .As<IAssettoServerAutostart>()
            .SingleInstance();
    }
}
