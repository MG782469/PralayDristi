using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateReservoir;

public sealed class EstimateReservoirQueryValidator : AbstractValidator<EstimateReservoirQuery>
{
    public EstimateReservoirQueryValidator()
    {
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x).Must(x => x.AreaKm2 != null || x.AltimetryLevel != null)
            .WithName("areaKm2").WithMessage("Give areaKm2 (from NDWI) or altimetryLevel (SWOT / ICESat-2).");
        RuleFor(x => x.AreaKm2).GreaterThan(0).LessThan(5000).When(x => x.AreaKm2 != null);
        RuleFor(x => x.DeadStorageMcm).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxRise).InclusiveBetween(10, 1000);
    }
}
