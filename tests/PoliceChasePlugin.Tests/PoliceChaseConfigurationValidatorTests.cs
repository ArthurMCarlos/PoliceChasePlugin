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
    public void AcceptsValidP05Configuration()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 12
        });

        Assert.That(result.IsValid, Is.True);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveMaximumDistance(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitMaxDistanceMeters = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitMaxDistanceMeters)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(1500)]
    [TestCase(1600)]
    public void RejectsDesiredDistanceOutsideMaximum(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitDesiredDistanceMeters = value,
            PursuitMaxDistanceMeters = 1500
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitDesiredDistanceMeters)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveMaximumSpeed(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitMaxSpeedKph = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitMaxSpeedKph)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsNonPositiveUpdateInterval(int value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitUpdateIntervalMilliseconds = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitUpdateIntervalMilliseconds)));
    }

    [TestCase(-1)]
    [TestCase(255)]
    public void RejectsInvalidPoliceSessionIdWhenEnabled(int sessionId)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            Enabled = true,
            PoliceCarSessionId = sessionId
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PoliceCarSessionId)));
    }

    [Test]
    public void AcceptsUnsetPoliceSessionIdWhenDisabled()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            Enabled = false,
            PoliceCarSessionId = -1
        });

        Assert.That(result.IsValid, Is.True);
    }
}
