using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadExposure;

public sealed class UploadExposureCommandValidator : AbstractValidator<UploadExposureCommand>
{
    public UploadExposureCommandValidator()
    {
        RuleFor(x => x.GeoJson).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(200);
    }
}
