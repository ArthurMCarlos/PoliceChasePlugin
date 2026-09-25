using PoliceChasePlugin.Players;

namespace PoliceChasePlugin.Tests.Players;

[TestFixture]
public class PoliceTargetSuppressionTests
{
    [Test]
    public void RefreshReconcilesReplacementBeforeDelayedDisconnect()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(10, "Original", true);
        var service = new PoliceTargetService(source, new Clock());
        service.Start();
        var previous = source.ReplaceBeforeDisconnect(10);
        service.Refresh();
        service.SuppressCurrentTarget(TimeSpan.FromSeconds(60));
        Assert.That(service.CurrentTargetSessionId, Is.Null);
        source.DeliverLateDisconnect(previous);
        service.Refresh();
        Assert.That(service.CurrentTargetSessionId, Is.Null);
    }

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }

    [Test]
    public void SameConnectionCannotRearmUntilSixtySecondsWithoutNewEvent()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(10, "Target", true);
        var clock = new Clock();
        var service = new PoliceTargetService(source, clock);
        service.Start();
        service.SuppressCurrentTarget(TimeSpan.FromSeconds(60));
        service.Refresh();
        Assert.That(service.CurrentTargetSessionId, Is.Null);
        clock.Now = 59999;
        service.Refresh();
        Assert.That(service.CurrentTargetSessionId, Is.Null);
        clock.Now = 60000;
        service.Refresh();
        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(10));
    }

    [Test]
    public void OtherPlayerAndNewConnectionDoNotInheritSuppression()
    {
        var source = new FakePolicePlayerSource();
        source.Connect(10, "Target", true);
        source.Connect(11, "Other", true);
        var service = new PoliceTargetService(source, new Clock());
        service.Start();
        service.SuppressCurrentTarget(TimeSpan.FromSeconds(60));
        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(11));
        source.Disconnect(11);
        source.Disconnect(10);
        source.Connect(10, "Reconnected", true);
        Assert.That(service.CurrentTargetSessionId, Is.EqualTo(10));
    }
}
