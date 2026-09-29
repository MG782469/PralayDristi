using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Data.GetDems;

public sealed class GetDemsQueryHandler : IRequestHandler<GetDemsQuery, JsonPayload>
{
    private readonly IDemProvider _dem;

    public GetDemsQueryHandler(IDemProvider dem) => _dem = dem;

    public Task<JsonPayload> Handle(GetDemsQuery request, CancellationToken ct) => _dem.ListUploadedAsync(ct);
}
