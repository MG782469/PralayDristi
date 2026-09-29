using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.InverseThreshold;

/// <summary>"How much has to be released before this bridge / camp / highway floods?"</summary>
public sealed record InverseThresholdQuery(Guid RunId, double Lon, double Lat, double Depth = 0.5, string? TargetName = null)
    : IRequest<JsonPayload>, ILongRunningRequest;
