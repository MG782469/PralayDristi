using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.InverseFromObserved;

public sealed class InverseFromObservedQueryValidator : AbstractValidator<InverseFromObservedQuery>
{
    public InverseFromObservedQueryValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.ObservedGeoJson).NotEmpty().MaximumLength(50_000_000);
    }
}
