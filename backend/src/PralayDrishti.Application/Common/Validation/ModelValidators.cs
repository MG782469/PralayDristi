using FluentValidation;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Common.Validation;

public sealed class ScenarioSpecValidator : AbstractValidator<ScenarioSpec>
{
    public ScenarioSpecValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.DurationHours).GreaterThan(0).LessThanOrEqualTo(72);
        RuleFor(x => x.MaxCells).InclusiveBetween(5_000, 3_000_000);
        RuleFor(x => x.EnsembleMembers).InclusiveBetween(0, 100);
        RuleFor(x => x.DownstreamKm).GreaterThan(0).LessThanOrEqualTo(300);
        RuleFor(x => x.UpstreamKm).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100);
        RuleFor(x => x.Manning).InclusiveBetween(0.01, 0.3);
        RuleFor(x => x.ChannelManning).InclusiveBetween(0.01, 0.3);
        RuleFor(x => x.SphResolution).InclusiveBetween(8, 80);
        RuleFor(x => x.DemZoom).InclusiveBetween(8, 14);
        RuleFor(x => x.WarningMinutes).InclusiveBetween(0, 600);

        When(x => x.Mode == ScenarioMode.NaturalDam, () =>
        {
            RuleFor(x => x.NaturalDam).NotNull().WithMessage("Natural dam mode needs the debris polygon.");
            RuleFor(x => x.NaturalDam!.DebrisPolygon).Must(p => p is { Count: >= 3 })
                .When(x => x.NaturalDam != null)
                .WithMessage("Draw the debris / landslide footprint (at least 3 points).");
            RuleForEach(x => x.NaturalDam!.DebrisPolygon).Must(ValidLonLat).When(x => x.NaturalDam != null)
                .WithMessage("Polygon vertices must be [lon, lat].");
        }).Otherwise(() =>
        {
            RuleFor(x => x.Dam).NotNull().SetValidator(new DamSpecValidator());
        });
        When(x => x.Mode == ScenarioMode.Cascade, () =>
            RuleFor(x => x.DownstreamDams).NotEmpty().WithMessage("Cascade mode needs at least one downstream dam."));
        RuleForEach(x => x.DownstreamDams).SetValidator(new DamSpecValidator());
        When(x => x.Structure != null, () =>
        {
            RuleFor(x => x.Structure!.DistanceKm).GreaterThan(0).LessThanOrEqualTo(50);
            RuleFor(x => x.Structure!.HeightM).GreaterThan(0);
            RuleFor(x => x.Structure!.WidthM).GreaterThan(0);
        });
    }

    internal static bool ValidLonLat(double[] p) => p.Length == 2 && Math.Abs(p[0]) <= 180 && Math.Abs(p[1]) <= 90;
}

public sealed class DamSpecValidator : AbstractValidator<DamSpec>
{
    public DamSpecValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.HeightM).GreaterThan(0).LessThanOrEqualTo(350);
        RuleFor(x => x.ReservoirVolumeM3).GreaterThan(0);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).NotEqual(0).WithMessage("Place the dam on the map.");
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180).NotEqual(0).WithMessage("Place the dam on the map.");
        RuleFor(x => x.CrestLengthM).GreaterThan(0);
        RuleFor(x => x.WaterDepthM).GreaterThan(0).LessThanOrEqualTo(x => x.HeightM * 1.1).When(x => x.WaterDepthM != null);
        RuleFor(x => x.BreachWidthM).GreaterThan(0).When(x => x.BreachWidthM != null);
        RuleFor(x => x.FormationTimeMin).GreaterThan(0).When(x => x.FormationTimeMin != null);
    }
}

public sealed class DamDtoValidator : AbstractValidator<DamDto>
{
    public DamDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
        RuleFor(x => x.River).MaximumLength(80);
        RuleFor(x => x.State).MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(400);
        RuleFor(x => x.Source).MaximumLength(200);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.HeightM).GreaterThan(0).LessThanOrEqualTo(350);
        RuleFor(x => x.GrossStorageMcm).GreaterThan(0);
        RuleFor(x => x.CrestLengthM).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Category).IsInEnum();
    }
}
