using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateNaturalDam;

public sealed class EstimateNaturalDamQueryValidator : AbstractValidator<EstimateNaturalDamQuery>
{
    public EstimateNaturalDamQueryValidator()
    {
        RuleFor(x => x.Polygon).NotNull().Must(p => p is { Count: >= 3 and <= 500 }).WithMessage("Polygon needs 3 to 500 vertices.");
        RuleForEach(x => x.Polygon).Must(p => p.Length == 2 && Math.Abs(p[0]) <= 180 && Math.Abs(p[1]) <= 90)
            .WithMessage("Vertices must be [lon, lat].");
        RuleFor(x => x.HeightAboveBed).InclusiveBetween(1, 500).When(x => x.HeightAboveBed != null);
    }
}
