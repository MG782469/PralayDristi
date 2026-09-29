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

namespace PralayDrishti.Application.Features.Queries.Runs.GetRiverProfile;

public sealed record GetRiverProfileQuery(Guid Id) : IRequest<JsonPayload>, ICacheableQuery
{
    public string CacheKey => CacheKeys.RunProfile(Id);
    public TimeSpan Expiry => TimeSpan.FromHours(1);
}
