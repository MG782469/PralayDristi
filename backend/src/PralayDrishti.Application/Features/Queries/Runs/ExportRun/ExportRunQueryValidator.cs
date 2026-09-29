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

namespace PralayDrishti.Application.Features.Queries.Runs.ExportRun;

public sealed class ExportRunQueryValidator : AbstractValidator<ExportRunQuery>
{
    private static readonly string[] Known = { "shp", "kmz", "kml", "geojson", "cap", "pdf", "csv", "asc", "delft3d", "dualsphysics" };

    public ExportRunQueryValidator() =>
        RuleFor(x => x.Format).Must(f => Known.Contains(f?.ToLowerInvariant())).WithMessage($"Format must be one of: {string.Join(", ", Known)}");
}
