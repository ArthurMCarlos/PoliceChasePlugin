using PoliceChasePlugin.Players;

namespace PoliceChasePlugin.Tests.Players;

[TestFixture]
public class PoliceTargetServiceTests
{
    [Test]
    public void SelectsLowestReadySessionIdOnStart()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(9, "Nine", ready: true);
        source.Connect(3, "Three", ready: true);
        var service = new PoliceTargetService(source);

        service.Start();

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(3));
    }

    [Test]
    public void WaitsForFirstUpdateBeforeAcquiringTarget()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);
        service.Start();

        source.Connect(4, "Loading");
        Assert.That(service.CurrentTargetSessionId, Is.Null);

        source.MarkReady(4);
        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(4));
    }

    [Test]
    public void KeepsCurrentTargetWhenAnotherPlayerBecomesReady()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);
        service.Start();
        source.Connect(7, "First", ready: true);
        source.MarkReady(7);

        source.Connect(2, "Later", ready: true);
        source.MarkReady(2);

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(7));
    }

    [Test]
    public void ReacquiresNextReadyPlayerWhenTargetDisconnects()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(8, "First", ready: true);
        source.Connect(10, "Second", ready: true);
        var service = new PoliceTargetService(source);
        service.Start();

        source.Disconnect(8);

        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(10));
    }

    [Test]
    public void ReturnsToWaitingWhenLastTargetDisconnects()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(1, "Only", ready: true);
        var service = new PoliceTargetService(source);
        service.Start();

        source.Disconnect(1);

        Assert.That(service.CurrentTargetSessionId, Is.Null);
    }

    [Test]
    public void StartAndStopAreIdempotent()
    {
        var source = new FakePolicePlayerSource();
        var service = new PoliceTargetService(source);

        service.Start();
        service.Start();
        service.Stop();
        service.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(source.StartCount, Is.EqualTo(1));
            Assert.That(source.StopCount, Is.EqualTo(1));
        });
    }
}
