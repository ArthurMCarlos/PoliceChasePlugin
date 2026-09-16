using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;
using PoliceChasePlugin.Ai;
using PoliceChasePlugin.Players;
using PoliceChasePlugin.Tests.Ai;
using PoliceChasePlugin.Tests.Players;

namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseModuleTests
{
    [Test]
    public void RegistersOneSharedAutostartService()
    {
        using var lifetime = new TestHostApplicationLifetime();
        var builder = new ContainerBuilder();
        builder.RegisterInstance(new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 7,
            PoliceCarModel = "police"
        });
        builder.RegisterInstance<IHostApplicationLifetime>(lifetime);

        var playerSource = new FakePolicePlayerSource();
        var aiSource = new FakePoliceAiSlotSource();
        aiSource.AddFixedSlot(7, "police");
        aiSource.States.Add(new FakePoliceAiState(false));

        builder.RegisterModule(new PoliceChaseModule());
        builder.RegisterInstance<IPolicePlayerSource>(playerSource);
        builder.RegisterInstance<IPoliceAiSlotSource>(aiSource);

        using var container = builder.Build();
        var byType = container.Resolve<PoliceChaseService>();
        var autostartServices = container.Resolve<IEnumerable<IAssettoServerAutostart>>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(autostartServices, Has.Length.EqualTo(1));
            Assert.That(autostartServices[0], Is.SameAs(byType));
            Assert.That(container.Resolve<PoliceTargetService>(), Is.Not.Null);
            Assert.That(container.Resolve<PoliceAiService>(), Is.Not.Null);
        });
    }
}
