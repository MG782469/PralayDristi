using System.Text.Json;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Runs.SamplePoint;

public sealed class SamplePointQueryValidator : AbstractValidator<SamplePointQuery>
{
    public SamplePointQueryValidator()
    {
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
    }
}
