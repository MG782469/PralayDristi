using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetSphCheck;

public sealed class GetSphCheckQueryHandler : IRequestHandler<GetSphCheckQuery, JsonPayload>
{
    private readonly IValidationBenchmarks _bench;

    public GetSphCheckQueryHandler(IValidationBenchmarks bench) => _bench = bench;

    public Task<JsonPayload> Handle(GetSphCheckQuery r, CancellationToken ct) => _bench.SphCheckAsync(r.Resolution, r.T, ct);
}
