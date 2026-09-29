using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetAnalyticCheck;

public sealed record GetAnalyticCheckQuery(double H0 = 10, double H1 = 0, double T = 8) : IRequest<JsonPayload>;
