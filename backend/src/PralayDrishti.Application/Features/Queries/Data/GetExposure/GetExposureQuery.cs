using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetExposure;

/// <summary>Exposure inside a box as GeoJSON for the map (points + roads). Pulls OpenStreetMap when asked and online.</summary>
public sealed record GetExposureQuery(BoundingBox Box, bool IncludeOsm) : IRequest<string>;
