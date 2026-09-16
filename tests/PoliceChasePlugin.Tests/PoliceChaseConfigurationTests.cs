namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationTests
{
    [Test]
    public void NewConfigurationUsesP0Defaults()
    {
        var configuration = new PoliceChaseConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(configuration.Enabled, Is.True);
            Assert.That(configuration.PoliceCarModel, Is.Empty);
            Assert.That(configuration.PoliceCarSessionId, Is.EqualTo(-1));
            Assert.That(configuration.MaxPoliceSpeedKph, Is.EqualTo(280));
            Assert.That(configuration.FarDistanceMeters, Is.EqualTo(800));
            Assert.That(configuration.MediumDistanceMeters, Is.EqualTo(400));
            Assert.That(configuration.NearDistanceMeters, Is.EqualTo(150));
            Assert.That(configuration.FarSpeedBonusKph, Is.EqualTo(60));
            Assert.That(configuration.MediumSpeedBonusKph, Is.EqualTo(40));
            Assert.That(configuration.NearSpeedBonusKph, Is.EqualTo(20));
            Assert.That(configuration.LostDistanceMeters, Is.EqualTo(2000));
            Assert.That(configuration.Debug, Is.True);
            Assert.That(configuration.DebugIntervalMs, Is.EqualTo(1000));
        });
    }
}
