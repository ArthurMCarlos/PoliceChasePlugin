using PoliceChasePlugin.Ai;

namespace PoliceChasePlugin.Tests;

[TestFixture]
public class PursuitCloseConfigurationTests
{
    [Test]
    public void DisabledModeDoesNotCreateOptions()
    {
        Assert.That(new PoliceChaseConfiguration().CreateClosePursuitOptions(), Is.Null);
    }

    [Test]
    public void ConvertsSpeedOnceAndKeepsPitOptional()
    {
        var config = Valid();
        var options = config.CreateClosePursuitOptions()!;
        var core = AssettoServerNativePolicePursuitState.MapCloseOptions(options);
        Assert.Multiple(() =>
        {
            Assert.That(core.MaxAdvantageMetersPerSecond, Is.EqualTo(80 / 3.6f));
            Assert.That(core.MaxSpeedMetersPerSecond, Is.EqualTo(400 / 3.6f));
            Assert.That(core.ObstacleDeficitMetersPerSecond, Is.EqualTo(10 / 3.6f));
            Assert.That(new PoliceChaseConfigurationValidator().Validate(config).IsValid, Is.True);
            Assert.That(config.PursuitPitEnabled, Is.False);
        });
    }

    static PoliceChaseConfiguration Valid() => new()
    {
        PoliceCarSessionId = 12, PursuitCloseEnabled = true,
        PursuitAggressiveDrivingEnabled = true
    };

    static IEnumerable<Action<PoliceChaseConfiguration>> Invalid()
    {
        foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            yield return c => c.PursuitCloseAssistStartMeters = value;
            yield return c => c.PursuitCloseAssistFullMeters = value;
            yield return c => c.PursuitCloseMaxAdvantageKph = value;
            yield return c => c.PursuitCloseMaxAccelerationMetersPerSecondSquared = value;
            yield return c => c.PursuitCloseMaxJerkMetersPerSecondCubed = value;
            yield return c => c.PursuitCloseMaxSpeedKph = value;
            yield return c => c.PursuitCloseObstacleDeficitKph = value;
            yield return c => c.PursuitCloseBypassLookaheadMeters = value;
            yield return c => c.PursuitCloseReturnClearanceMeters = value;
            yield return c => c.PursuitCloseEscapeDistanceMeters = value;
        }
        yield return c => c.PursuitCloseAssistStartMeters = 15;
        yield return c => c.PursuitCloseAssistFullMeters = 60;
        yield return c => c.PursuitCloseEscapeDistanceMeters = 250;
        yield return c => c.PursuitCloseEscapeDistanceMeters = 1500;
        yield return c => c.PursuitCloseBypassLookaheadMeters = 59;
        yield return c => c.PursuitCloseMaxSpeedKph = 179;
        yield return c => c.PursuitCloseObstacleHoldMilliseconds = 0;
        yield return c => c.PursuitCloseEscapeHoldMilliseconds = 0;
        yield return c => c.PursuitCloseRearmDelayMilliseconds = 0;
        yield return c => c.PursuitAggressiveDrivingEnabled = false;
        yield return c => c.PursuitLaneChangeEnabled = false;
    }

    [TestCaseSource(nameof(Invalid))]
    public void RejectsInvalidEnabledConfiguration(Action<PoliceChaseConfiguration> change)
    {
        var config = Valid();
        change(config);
        Assert.That(new PoliceChaseConfigurationValidator().Validate(config).IsValid, Is.False);
        config.PursuitCloseEnabled = false;
        Assert.That(new PoliceChaseConfigurationValidator().Validate(config).IsValid, Is.True);
    }
}
