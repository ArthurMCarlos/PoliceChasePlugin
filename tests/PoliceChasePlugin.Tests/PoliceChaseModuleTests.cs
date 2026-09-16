using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseModuleTests
{
    [Test]
    public void RegistersOneSharedAutostartService()
    {
        using var lifetime = new TestHostApplicationLifetime();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new PoliceChaseConfiguration());
        builder.RegisterInstance<IHostApplicationLifetime>(lifetime);
        builder.RegisterModule(new PoliceChaseModule());

        using var container = builder.Build();
        var byType = container.Resolve<PoliceChaseService>();
        var autostartServices = container.Resolve<IEnumerable<IAssettoServerAutostart>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(autostartServices, Has.Length.EqualTo(1));
            Assert.That(autostartServices[0], Is.SameAs(byType));
        });
    }
}
