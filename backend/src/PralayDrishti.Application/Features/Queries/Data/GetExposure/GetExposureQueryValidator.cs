using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetExposure;

public sealed class GetExposureQueryValidator : AbstractValidator<GetExposureQuery>
{
    public GetExposureQueryValidator()
    {
        RuleFor(x => x.Box.East).GreaterThan(x => x.Box.West);
        RuleFor(x => x.Box.North).GreaterThan(x => x.Box.South);
        RuleFor(x => x.Box).Must(b => b.East - b.West <= 2 && b.North - b.South <= 2).WithMessage("Box too large (max 2 x 2 degrees).");
    }
}
