using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.CompareWithObserved;

public sealed class CompareWithObservedCommandValidator : AbstractValidator<CompareWithObservedCommand>
{
    public CompareWithObservedCommandValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.ObservedGeoJson).NotEmpty();
        RuleFor(x => x.EventName).MaximumLength(160);
        RuleFor(x => x.DepthThreshold).InclusiveBetween(0.01, 5);
    }
}
