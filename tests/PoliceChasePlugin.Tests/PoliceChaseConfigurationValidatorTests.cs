namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PoliceChaseConfigurationValidatorTests
{
    private PoliceChaseConfigurationValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new PoliceChaseConfigurationValidator();
    }

    [Test]
    public void AcceptsValidP0Configuration()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration());

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveMaximumPoliceSpeed(float speed)
    {
        var configuration = new PoliceChaseConfiguration { MaxPoliceSpeedKph = speed };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(nameof(PoliceChaseConfiguration.MaxPoliceSpeedKph)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveDebugInterval(int interval)
    {
        var configuration = new PoliceChaseConfiguration { DebugIntervalMs = interval };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(nameof(PoliceChaseConfiguration.DebugIntervalMs)));
    }

    [TestCase(150, 150, 800, 2000, "NearDistanceMeters")]
    [TestCase(150, 800, 800, 2000, "MediumDistanceMeters")]
    [TestCase(150, 400, 2000, 2000, "FarDistanceMeters")]
    [TestCase(150, 400, 800, 0, "LostDistanceMeters")]
    [TestCase(0, 400, 800, 2000, "NearDistanceMeters")]
    public void RejectsInvalidDistanceOrder(float near, float medium, float far, float lost, string expectedProperty)
    {
        var configuration = new PoliceChaseConfiguration
        {
            NearDistanceMeters = near,
            MediumDistanceMeters = medium,
            FarDistanceMeters = far,
            LostDistanceMeters = lost
        };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(expectedProperty));
    }

    [TestCase(-1, 40, 20, "FarSpeedBonusKph")]
    [TestCase(60, -1, 20, "MediumSpeedBonusKph")]
    [TestCase(60, 40, -1, "NearSpeedBonusKph")]
    public void RejectsNegativeSpeedBonus(float far, float medium, float near, string expectedProperty)
    {
        var configuration = new PoliceChaseConfiguration
        {
            FarSpeedBonusKph = far,
            MediumSpeedBonusKph = medium,
            NearSpeedBonusKph = near
        };

        var result = _validator.Validate(configuration);

        Assert.That(result.Errors, Has.Some.Property("PropertyName").EqualTo(expectedProperty));
    }
}
