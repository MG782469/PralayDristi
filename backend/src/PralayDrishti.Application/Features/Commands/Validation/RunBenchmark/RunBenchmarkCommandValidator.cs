using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.RunBenchmark;

public sealed class RunBenchmarkCommandValidator : AbstractValidator<RunBenchmarkCommand>
{
    public RunBenchmarkCommandValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).Matches("^[A-Za-z0-9_-]+$").WithMessage("Letters, digits, - and _ only.");
}
