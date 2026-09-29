using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.EstimateNaturalDam;

/// <summary>Natural-dam mode, step 1: how big can the lake get behind the debris the user drew?</summary>
public sealed record EstimateNaturalDamQuery(List<double[]> Polygon, double? CrestElevation, double? HeightAboveBed, double? CurrentLevel,
    DamType Type = DamType.LandslideDam) : IRequest<JsonPayload>, ILongRunningRequest;
