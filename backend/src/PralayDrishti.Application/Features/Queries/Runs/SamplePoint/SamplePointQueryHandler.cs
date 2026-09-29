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

namespace PralayDrishti.Application.Features.Queries.Runs.SamplePoint;

public sealed class SamplePointQueryHandler : IRequestHandler<SamplePointQuery, JsonPayload>
{
    private readonly IResultStore _store;

    public SamplePointQueryHandler(IResultStore store) => _store = store;

    public async Task<JsonPayload> Handle(SamplePointQuery request, CancellationToken ct) =>
        await _store.SamplePointAsync(request.Id, request.Lon, request.Lat, ct) ?? throw new NotFoundException("Run results", request.Id);
}
