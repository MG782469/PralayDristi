using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.CalculateBreach;

public sealed class CalculateBreachQueryValidator : AbstractValidator<CalculateBreachQuery>
{
    public CalculateBreachQueryValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.HeightM).GreaterThan(0).LessThanOrEqualTo(350);
        RuleFor(x => x.VolumeMcm).GreaterThan(0);
        RuleFor(x => x.WaterDepthM).GreaterThan(0).LessThanOrEqualTo(x => x.HeightM * 1.1).When(x => x.WaterDepthM != null);
        RuleFor(x => x.CrestLengthM).GreaterThan(0);
        RuleFor(x => x.StorageExponent).InclusiveBetween(1, 4);
    }
}
