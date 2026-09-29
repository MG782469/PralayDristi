using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Commands.Auth.RefreshToken;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(200);
}
