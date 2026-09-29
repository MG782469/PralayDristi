using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.InverseThreshold;

public sealed class InverseThresholdQueryValidator : AbstractValidator<InverseThresholdQuery>
{
    public InverseThresholdQueryValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Depth).GreaterThan(0).LessThanOrEqualTo(50);
        RuleFor(x => x.TargetName).MaximumLength(120);
    }
}
