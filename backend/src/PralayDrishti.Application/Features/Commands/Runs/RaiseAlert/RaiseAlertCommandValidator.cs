using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Runs.RaiseAlert;

public sealed class RaiseAlertCommandValidator : AbstractValidator<RaiseAlertCommand>
{
    public RaiseAlertCommandValidator()
    {
        RuleFor(x => x.RunId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
    }
}
