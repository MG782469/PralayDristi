using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Lakes.GetWaterArea;

public sealed class GetWaterAreaQueryValidator : AbstractValidator<GetWaterAreaQuery>
{
    public GetWaterAreaQueryValidator()
    {
        RuleFor(x => x.Ndwi).NotNull();
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Threshold).InclusiveBetween(-1, 1);
    }
}
