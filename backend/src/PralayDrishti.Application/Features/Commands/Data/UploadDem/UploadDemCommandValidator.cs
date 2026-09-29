using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadDem;

public sealed class UploadDemCommandValidator : AbstractValidator<UploadDemCommand>
{
    public UploadDemCommandValidator()
    {
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(200)
            .Must(n => n.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Upload a GeoTIFF (.tif / .tiff).");
    }
}
