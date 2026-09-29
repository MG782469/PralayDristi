using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetExposure;

public sealed class GetExposureQueryHandler : IRequestHandler<GetExposureQuery, string>
{
    private readonly IExposureProvider _exposure;

    public GetExposureQueryHandler(IExposureProvider exposure) => _exposure = exposure;

    public Task<string> Handle(GetExposureQuery request, CancellationToken ct) => _exposure.GetGeoJsonAsync(request.Box, request.IncludeOsm, ct);
}
