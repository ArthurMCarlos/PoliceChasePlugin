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
    public void AcceptsValidP06Configuration()
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

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectsInvalidRouteSearchDistance(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitRouteSearchMaxDistanceMeters = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitRouteSearchMaxDistanceMeters)));
    }

    [Test]
    public void RejectsRouteSearchDistanceBelowSpatialMaximum()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitMaxDistanceMeters = 1500,
            PursuitRouteSearchMaxDistanceMeters = 1499
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitRouteSearchMaxDistanceMeters)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsInvalidRouteNodeBudget(int value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitRouteSearchMaxVisitedNodes = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitRouteSearchMaxVisitedNodes)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsInvalidRouteGrace(int value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitRouteGraceMilliseconds = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitRouteGraceMilliseconds)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void RejectsInvalidNoRouteProbeInterval(int value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitNoRouteProbeIntervalMilliseconds = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitNoRouteProbeIntervalMilliseconds)));
    }

    [TestCase(19)]
    [TestCase(201)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectsInvalidLaneChangeDistance(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitLaneChangeDistanceMeters = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitLaneChangeDistanceMeters)));
    }

    [TestCase(-1)]
    [TestCase(30_001)]
    public void RejectsInvalidLaneChangeCooldown(int value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitLaneChangeCooldownMilliseconds = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitLaneChangeCooldownMilliseconds)));
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
