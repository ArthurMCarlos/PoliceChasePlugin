namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationTests
{
    [Test]
    public void NewConfigurationUsesP05Defaults()
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
        });
    }
}
