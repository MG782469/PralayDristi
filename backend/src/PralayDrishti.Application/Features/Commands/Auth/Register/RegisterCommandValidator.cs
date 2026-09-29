using PralayDrishti.Application.Common.Validation;
using FluentValidation;

namespace PralayDrishti.Application.Features.Commands.Auth.Register;

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Password).ApplyPasswordRules();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(160);
    }
}
