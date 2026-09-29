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

public sealed class GetRiverProfileQueryHandler : IRequestHandler<GetRiverProfileQuery, JsonPayload>
{
    private readonly IResultStore _store;

    public GetRiverProfileQueryHandler(IResultStore store) => _store = store;

    public async Task<JsonPayload> Handle(GetRiverProfileQuery request, CancellationToken ct) =>
        await _store.GetProfileAsync(request.Id, ct) ?? throw new NotFoundException("Run results", request.Id);
}
