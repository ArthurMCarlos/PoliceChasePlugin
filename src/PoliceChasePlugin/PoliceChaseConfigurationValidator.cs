using FluentValidation;
using JetBrains.Annotations;

namespace PoliceChasePlugin;

[UsedImplicitly]
public class PoliceChaseConfigurationValidator : AbstractValidator<PoliceChaseConfiguration>
{
    public PoliceChaseConfigurationValidator()
    {
        When(configuration => configuration.Enabled, () =>
        {
            RuleFor(configuration => configuration.PoliceCarSessionId)
                .InclusiveBetween(0, 254);
        });
        RuleFor(configuration => configuration.PursuitMaxDistanceMeters)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitDesiredDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.PursuitMaxDistanceMeters);
        RuleFor(configuration => configuration.PursuitMaxSpeedKph)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitUpdateIntervalMilliseconds)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitRouteSearchMaxDistanceMeters)
            .Must(float.IsFinite)
            .GreaterThanOrEqualTo(configuration => configuration.PursuitMaxDistanceMeters);
        RuleFor(configuration => configuration.PursuitRouteSearchMaxVisitedNodes)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitRouteGraceMilliseconds)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitNoRouteProbeIntervalMilliseconds)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitLaneChangeDistanceMeters)
            .Must(float.IsFinite)
            .InclusiveBetween(20, 200);
        RuleFor(configuration => configuration.PursuitLaneChangeCooldownMilliseconds)
            .InclusiveBetween(0, 30_000);
        RuleFor(configuration => configuration.PursuitLaneChangeLookaheadMeters)
            .Must(float.IsFinite)
            .InclusiveBetween(100, 5_000)
            .GreaterThanOrEqualTo(configuration =>
                configuration.PursuitLaneChangeDistanceMeters);
        RuleFor(configuration => configuration.PursuitCatchUpDistanceMeters)
            .Cascade(CascadeMode.Stop)
            .Must(float.IsFinite)
            .GreaterThan(configuration => configuration.PursuitCloseDistanceMeters);
        RuleFor(configuration => configuration.PursuitCloseDistanceMeters)
            .Cascade(CascadeMode.Stop)
            .Must(float.IsFinite)
            .GreaterThan(configuration => configuration.PursuitContactDistanceMeters);
        RuleFor(configuration => configuration.PursuitContactDistanceMeters)
            .Cascade(CascadeMode.Stop)
            .Must(float.IsFinite)
            .GreaterThan(0);
        RuleFor(configuration => configuration.PursuitMaxClosingSpeedKph)
            .Cascade(CascadeMode.Stop)
            .Must(float.IsFinite)
            .GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.PursuitContactClosingSpeedKph)
            .Cascade(CascadeMode.Stop)
            .Must(float.IsFinite)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(configuration =>
                configuration.PursuitMaxClosingSpeedKph);
    }
}
