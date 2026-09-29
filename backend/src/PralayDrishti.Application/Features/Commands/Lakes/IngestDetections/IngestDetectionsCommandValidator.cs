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

namespace PralayDrishti.Application.Features.Commands.Lakes.IngestDetections;

public sealed class IngestDetectionsCommandValidator : AbstractValidator<IngestDetectionsCommand>
{
    public IngestDetectionsCommandValidator()
    {
        RuleFor(x => x.Detections).NotEmpty().Must(d => d.Count <= 200).WithMessage("At most 200 detections per request.");
        RuleForEach(x => x.Detections).ChildRules(d =>
        {
            d.RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
            d.RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
            d.RuleFor(x => x.AreaKm2).GreaterThan(0).LessThan(5000);
        });
    }
}
