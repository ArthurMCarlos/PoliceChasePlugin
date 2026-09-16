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
        RuleFor(configuration => configuration.MaxPoliceSpeedKph).GreaterThan(0);
        RuleFor(configuration => configuration.NearDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.MediumDistanceMeters);
        RuleFor(configuration => configuration.MediumDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.FarDistanceMeters);
        RuleFor(configuration => configuration.FarDistanceMeters)
            .GreaterThan(0)
            .LessThan(configuration => configuration.LostDistanceMeters);
        RuleFor(configuration => configuration.LostDistanceMeters).GreaterThan(0);
        RuleFor(configuration => configuration.FarSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.MediumSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.NearSpeedBonusKph).GreaterThanOrEqualTo(0);
        RuleFor(configuration => configuration.DebugIntervalMs).GreaterThan(0);
    }
}
