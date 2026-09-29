using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Models;

namespace PralayDrishti.Application.Features.Commands.Validation.RunBenchmark;

public sealed class RunBenchmarkCommandHandler : IRequestHandler<RunBenchmarkCommand, JsonPayload>
{
    private readonly IValidationBenchmarks _bench;

    public RunBenchmarkCommandHandler(IValidationBenchmarks bench) => _bench = bench;

    public Task<JsonPayload> Handle(RunBenchmarkCommand r, CancellationToken ct) => _bench.RunBenchmarkAsync(r.Name, ct);
}
