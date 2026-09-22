namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationTests
{
    [Test]
    public void NewConfigurationUsesP08Defaults()
    {
        var configuration = new PoliceChaseConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(configuration.Enabled, Is.True);
            Assert.That(configuration.PoliceCarModel, Is.Empty);
            Assert.That(configuration.PoliceCarSessionId, Is.EqualTo(-1));
            Assert.That(configuration.PursuitMaxDistanceMeters, Is.EqualTo(1500));
            Assert.That(configuration.PursuitDesiredDistanceMeters, Is.EqualTo(50));
            Assert.That(configuration.PursuitMaxSpeedKph, Is.EqualTo(180));
            Assert.That(configuration.PursuitUpdateIntervalMilliseconds, Is.EqualTo(200));
            Assert.That(configuration.PursuitRouteSearchMaxDistanceMeters, Is.EqualTo(20_000));
            Assert.That(configuration.PursuitRouteSearchMaxVisitedNodes, Is.EqualTo(50_000));
            Assert.That(configuration.PursuitRouteGraceMilliseconds, Is.EqualTo(2_000));
            Assert.That(configuration.PursuitNoRouteProbeIntervalMilliseconds, Is.EqualTo(2_000));
            Assert.That(configuration.PursuitLaneChangeEnabled, Is.True);
            Assert.That(configuration.PursuitLaneChangeDistanceMeters, Is.EqualTo(60));
            Assert.That(configuration.PursuitLaneChangeCooldownMilliseconds, Is.EqualTo(3_000));
            Assert.That(configuration.PursuitLaneChangeLookaheadMeters, Is.EqualTo(1_000));
            Assert.That(configuration.PursuitAggressiveDrivingEnabled, Is.False);
            Assert.That(configuration.PursuitContactEnabled, Is.True);
            Assert.That(configuration.PursuitCatchUpDistanceMeters, Is.EqualTo(100));
            Assert.That(configuration.PursuitCloseDistanceMeters, Is.EqualTo(15));
            Assert.That(configuration.PursuitContactDistanceMeters, Is.EqualTo(3));
            Assert.That(configuration.PursuitMaxClosingSpeedKph, Is.EqualTo(35));
            Assert.That(configuration.PursuitContactClosingSpeedKph, Is.EqualTo(5));
        });
    }
}
