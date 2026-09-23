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

    [TestCase(99)]
    [TestCase(5001)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RejectsInvalidLaneChangeLookahead(float value)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitLaneChangeLookaheadMeters = value
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitLaneChangeLookaheadMeters)));
    }

    [Test]
    public void RejectsLaneChangeLookaheadShorterThanTransition()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitLaneChangeDistanceMeters = 120,
            PursuitLaneChangeLookaheadMeters = 100
        });

        Assert.That(result.Errors, Has.Some.Property("PropertyName")
            .EqualTo(nameof(PoliceChaseConfiguration.PursuitLaneChangeLookaheadMeters)));
    }

    [TestCase(15, 15, 3, nameof(PoliceChaseConfiguration.PursuitCatchUpDistanceMeters))]
    [TestCase(100, 3, 3, nameof(PoliceChaseConfiguration.PursuitCloseDistanceMeters))]
    [TestCase(100, 15, 0, nameof(PoliceChaseConfiguration.PursuitContactDistanceMeters))]
    [TestCase(float.NaN, 15, 3, nameof(PoliceChaseConfiguration.PursuitCatchUpDistanceMeters))]
    [TestCase(100, float.PositiveInfinity, 3, nameof(PoliceChaseConfiguration.PursuitCloseDistanceMeters))]
    [TestCase(100, 15, float.NaN, nameof(PoliceChaseConfiguration.PursuitContactDistanceMeters))]
    public void DrivingDistancesMustBeFinitePositiveAndStrictlyDescending(
        float catchUp,
        float close,
        float contact,
        string expectedProperty)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitCatchUpDistanceMeters = catchUp,
            PursuitCloseDistanceMeters = close,
            PursuitContactDistanceMeters = contact
        });

        Assert.That(result.Errors.Select(error => error.PropertyName),
            Does.Contain(expectedProperty));
    }

    [TestCase(-1, 5, nameof(PoliceChaseConfiguration.PursuitMaxClosingSpeedKph))]
    [TestCase(35, -1, nameof(PoliceChaseConfiguration.PursuitContactClosingSpeedKph))]
    [TestCase(35, 36, nameof(PoliceChaseConfiguration.PursuitContactClosingSpeedKph))]
    [TestCase(float.NaN, 5, nameof(PoliceChaseConfiguration.PursuitMaxClosingSpeedKph))]
    [TestCase(35, float.PositiveInfinity, nameof(PoliceChaseConfiguration.PursuitContactClosingSpeedKph))]
    public void DrivingClosingSpeedsMustBeFiniteNonNegativeAndOrdered(
        float maximum,
        float contact,
        string expectedProperty)
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PursuitMaxClosingSpeedKph = maximum,
            PursuitContactClosingSpeedKph = contact
        });

        Assert.That(result.Errors.Select(error => error.PropertyName),
            Does.Contain(expectedProperty));
    }

    [Test]
    public void EnabledPitRequiresAggressiveDrivingAndContact()
    {
        var configuration = new PoliceChaseConfiguration { PursuitPitEnabled = true };
        var noAggressive = _validator.Validate(configuration);
        configuration.PursuitAggressiveDrivingEnabled = true;
        configuration.PursuitContactEnabled = false;
        var noContact = _validator.Validate(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(noAggressive.Errors.Select(error => error.PropertyName),
                Does.Contain(nameof(PoliceChaseConfiguration.PursuitPitEnabled)));
            Assert.That(noContact.Errors.Select(error => error.PropertyName),
                Does.Contain(nameof(PoliceChaseConfiguration.PursuitPitEnabled)));
        });
    }

    [TestCase(3, nameof(PoliceChaseConfiguration.PursuitPitMaxDistanceMeters))]
    [TestCase(float.NaN, nameof(PoliceChaseConfiguration.PursuitPitMaxDistanceMeters))]
    [TestCase(0, nameof(PoliceChaseConfiguration.PursuitPitMaxClosingSpeedKph))]
    [TestCase(float.PositiveInfinity, nameof(PoliceChaseConfiguration.PursuitPitMaxClosingSpeedKph))]
    [TestCase(0, nameof(PoliceChaseConfiguration.PursuitPitLateralOffsetMeters))]
    [TestCase(1.2f, nameof(PoliceChaseConfiguration.PursuitPitLateralOffsetMeters))]
    public void RejectsUnsafePitMagnitude(float value, string property)
    {
        var configuration = new PoliceChaseConfiguration
        {
            PursuitAggressiveDrivingEnabled = true,
            PursuitPitEnabled = true
        };
        switch (property)
        {
            case nameof(PoliceChaseConfiguration.PursuitPitMaxDistanceMeters):
                configuration.PursuitPitMaxDistanceMeters = value;
                break;
            case nameof(PoliceChaseConfiguration.PursuitPitMaxClosingSpeedKph):
                configuration.PursuitPitMaxClosingSpeedKph = value;
                break;
            case nameof(PoliceChaseConfiguration.PursuitPitLateralOffsetMeters):
                configuration.PursuitPitLateralOffsetMeters = value;
                break;
        }

        Assert.That(_validator.Validate(configuration).Errors.Select(error => error.PropertyName),
            Does.Contain(property));
    }

    [TestCase(199, nameof(PoliceChaseConfiguration.PursuitPitCommitMilliseconds))]
    [TestCase(5001, nameof(PoliceChaseConfiguration.PursuitPitCommitMilliseconds))]
    [TestCase(-1, nameof(PoliceChaseConfiguration.PursuitPitCooldownMilliseconds))]
    [TestCase(30001, nameof(PoliceChaseConfiguration.PursuitPitCooldownMilliseconds))]
    public void RejectsUnsafePitTiming(int value, string property)
    {
        var configuration = new PoliceChaseConfiguration();
        if (property == nameof(PoliceChaseConfiguration.PursuitPitCommitMilliseconds))
            configuration.PursuitPitCommitMilliseconds = value;
        else
            configuration.PursuitPitCooldownMilliseconds = value;

        Assert.That(_validator.Validate(configuration).Errors.Select(error => error.PropertyName),
            Does.Contain(property));
    }

    [Test]
    public void AcceptsOptedInPitWithSafeDefaults()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 12,
            PursuitAggressiveDrivingEnabled = true,
            PursuitPitEnabled = true
        });

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void DisabledPitDoesNotInvalidateExistingCloseDistanceConfiguration()
    {
        var result = _validator.Validate(new PoliceChaseConfiguration
        {
            PoliceCarSessionId = 12,
            PursuitAggressiveDrivingEnabled = true,
            PursuitCloseDistanceMeters = 5,
            PursuitContactDistanceMeters = 3,
            PursuitPitEnabled = false
        });

        Assert.That(result.IsValid, Is.True);
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
