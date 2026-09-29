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

/// <summary>Offline change detection when there is no GEE access: before/after NDWI GeoTIFFs on the same grid.</summary>
public sealed record DetectFromNdwiCommand(Stream Before, Stream After, double Threshold, double MinAreaKm2, bool AutoSimulate)
    : IRequest<IReadOnlyList<Guid>>;
