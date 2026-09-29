using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.CalculateBreach;

/// <summary>Breach calculator: every empirical method side by side, each routed through the reservoir.</summary>
public sealed record CalculateBreachQuery(DamType Type, double HeightM, double? WaterDepthM, double VolumeMcm,
    FailureMode Mode = FailureMode.Overtopping, double CrestLengthM = 300, double CrestWidthM = 10, double StorageExponent = 2.5)
    : IRequest<JsonPayload>;
