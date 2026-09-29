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

namespace PralayDrishti.Application.Features.Commands.Lakes.DismissLake;

public sealed class DismissLakeCommandHandler : IRequestHandler<DismissLakeCommand, Unit>
{
    private readonly ILakeDetectionRepository _lakes;
    private readonly IUnitOfWork _uow;

    public DismissLakeCommandHandler(ILakeDetectionRepository lakes, IUnitOfWork uow)
    {
        _lakes = lakes;
        _uow = uow;
    }

    public async Task<Unit> Handle(DismissLakeCommand request, CancellationToken ct)
    {
        var lake = await _lakes.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Lake detection", request.Id);
        lake.State = LakeState.Dismissed;
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
