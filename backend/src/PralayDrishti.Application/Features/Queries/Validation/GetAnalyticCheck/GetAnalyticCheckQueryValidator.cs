using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetAnalyticCheck;

public sealed class GetAnalyticCheckQueryValidator : AbstractValidator<GetAnalyticCheckQuery>
{
    public GetAnalyticCheckQueryValidator()
    {
        RuleFor(x => x.H0).GreaterThan(0).LessThanOrEqualTo(300);
        RuleFor(x => x.H1).GreaterThanOrEqualTo(0).LessThan(x => x.H0).WithMessage("Downstream depth must be below the reservoir depth.");
        RuleFor(x => x.T).GreaterThan(0).LessThanOrEqualTo(60);
    }
}
