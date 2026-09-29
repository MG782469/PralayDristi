using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetSphCheck;

public sealed record GetSphCheckQuery(int Resolution = 20, double T = 1.0) : IRequest<JsonPayload>, ILongRunningRequest;
