using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Options;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Lakes.SimulateLake;

public sealed class SimulateLakeCommandHandler : IRequestHandler<SimulateLakeCommand, Guid>
{
    private readonly LakeToAlertService _chain;

    public SimulateLakeCommandHandler(LakeToAlertService chain) => _chain = chain;

    public Task<Guid> Handle(SimulateLakeCommand request, CancellationToken ct) => _chain.EstimateAndQueueAsync(request.Id, ct);
}
