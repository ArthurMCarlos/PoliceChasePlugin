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
    }
}
