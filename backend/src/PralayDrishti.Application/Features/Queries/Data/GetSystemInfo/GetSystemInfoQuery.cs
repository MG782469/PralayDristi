using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetSystemInfo;

public sealed record GetSystemInfoQuery : IRequest<SystemInfoDto>, ICacheableQuery
{
    public string CacheKey => CacheKeys.SystemInfo;
    public TimeSpan Expiry => TimeSpan.FromSeconds(30);
}
