using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Queries.Validation.GetAnalyticCheck;

public sealed class GetAnalyticCheckQueryHandler : IRequestHandler<GetAnalyticCheckQuery, JsonPayload>
{
    private readonly IValidationBenchmarks _bench;

    public GetAnalyticCheckQueryHandler(IValidationBenchmarks bench) => _bench = bench;

    public Task<JsonPayload> Handle(GetAnalyticCheckQuery r, CancellationToken ct) => Task.FromResult(_bench.Analytic(r.H0, r.H1, r.T));
}
