using PoliceChasePlugin.Pursuit;

namespace PoliceChasePlugin.Tests.Pursuit;

[TestFixture]
public class PolicePursuitSpeedPolicyTests
{
    [TestCase(50, 20, 50, 180, 20)]
    [TestCase(150, 20, 50, 180, 45)]
    [TestCase(1000, 60, 50, 180, 180)]
    public void CalculatesProgressiveCappedSpeed(
        float distanceMeters,
        float targetSpeedKph,
        float desiredDistanceMeters,
        float maximumSpeedKph,
        float expectedKph)
    {
        var actual = PolicePursuitSpeedPolicy.Calculate(
            targetSpeedKph / 3.6f,
            distanceMeters,
            desiredDistanceMeters,
            maximumSpeedKph);

        Assert.That(actual * 3.6f, Is.EqualTo(expectedKph).Within(0.01));
    }

    [TestCase(float.NaN, 100, 50, 180)]
    [TestCase(10, float.PositiveInfinity, 50, 180)]
    [TestCase(10, 100, 0, 180)]
    [TestCase(10, 100, 50, -1)]
    public void RejectsInvalidInputs(
        float targetSpeedMetersPerSecond,
        float distanceMeters,
        float desiredDistanceMeters,
        float maximumSpeedKph)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PolicePursuitSpeedPolicy.Calculate(
                targetSpeedMetersPerSecond,
                distanceMeters,
                desiredDistanceMeters,
                maximumSpeedKph));
    }
}
