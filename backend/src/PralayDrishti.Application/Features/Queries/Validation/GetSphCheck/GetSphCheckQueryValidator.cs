using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetSphCheck;

public sealed class GetSphCheckQueryValidator : AbstractValidator<GetSphCheckQuery>
{
    public GetSphCheckQueryValidator()
    {
        RuleFor(x => x.Resolution).InclusiveBetween(10, 40);
        RuleFor(x => x.T).InclusiveBetween(0.2, 2);
    }
}
