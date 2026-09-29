using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Application.Features.Queries.Analysis.InverseFromObserved;

/// <summary>"Satellite shows this flood - how much was released upstream?" (best CSI over the release scale)</summary>
public sealed record InverseFromObservedQuery(Guid RunId, string ObservedGeoJson) : IRequest<JsonPayload>, ILongRunningRequest;
