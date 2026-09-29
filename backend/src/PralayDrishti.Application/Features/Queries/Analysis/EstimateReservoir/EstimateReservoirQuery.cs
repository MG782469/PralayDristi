using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateReservoir;

/// <summary>Transboundary / no-ground-data reservoirs: satellite area or altimetry level + DEM -> volume.</summary>
public sealed record EstimateReservoirQuery(double Lon, double Lat, double? AreaKm2, double? AltimetryLevel, double DeadStorageMcm = 0,
    double MaxRise = 300) : IRequest<JsonPayload>, ILongRunningRequest;
