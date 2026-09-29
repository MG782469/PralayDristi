using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetScoreboard;

public sealed class GetScoreboardQueryHandler : IRequestHandler<GetScoreboardQuery, JsonPayload>
{
    private readonly IValidationBenchmarks _bench;

    public GetScoreboardQueryHandler(IValidationBenchmarks bench) => _bench = bench;

    public Task<JsonPayload> Handle(GetScoreboardQuery request, CancellationToken ct) => _bench.ScoreboardAsync(ct);
}
