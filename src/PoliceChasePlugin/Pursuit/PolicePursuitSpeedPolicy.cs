namespace PoliceChasePlugin.Pursuit;

public static class PolicePursuitSpeedPolicy
{
    private const float CatchUpGainKphPerMeter = 0.25f;

    public static float Calculate(
        float targetSpeedMetersPerSecond,
        float routeDistanceMeters,
        float desiredDistanceMeters,
        float maximumSpeedKph)
    {
        ValidateNonNegativeFinite(targetSpeedMetersPerSecond, nameof(targetSpeedMetersPerSecond));
        ValidateNonNegativeFinite(routeDistanceMeters, nameof(routeDistanceMeters));
        ValidatePositiveFinite(desiredDistanceMeters, nameof(desiredDistanceMeters));
        ValidatePositiveFinite(maximumSpeedKph, nameof(maximumSpeedKph));

        var targetSpeedKph = targetSpeedMetersPerSecond * 3.6f;
        var distanceError = Math.Max(0, routeDistanceMeters - desiredDistanceMeters);
        var requestedSpeedKph = targetSpeedKph + distanceError * CatchUpGainKphPerMeter;
        return Math.Min(requestedSpeedKph, maximumSpeedKph) / 3.6f;
    }

    private static void ValidateNonNegativeFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidatePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
