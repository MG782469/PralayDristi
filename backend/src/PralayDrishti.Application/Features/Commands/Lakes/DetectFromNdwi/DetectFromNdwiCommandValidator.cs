using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Lakes.DetectFromNdwi;

public sealed class DetectFromNdwiCommandValidator : AbstractValidator<DetectFromNdwiCommand>
{
    public DetectFromNdwiCommandValidator()
    {
        RuleFor(x => x.Before).NotNull();
        RuleFor(x => x.After).NotNull();
        RuleFor(x => x.Threshold).InclusiveBetween(-1, 1);
        RuleFor(x => x.MinAreaKm2).GreaterThan(0);
    }
}
