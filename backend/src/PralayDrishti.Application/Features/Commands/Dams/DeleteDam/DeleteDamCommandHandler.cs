using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Application.Common.Validation;
using PralayDrishti.Application.Services;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Commands.Dams.DeleteDam;

public sealed class DeleteDamCommandHandler : IRequestHandler<DeleteDamCommand, Unit>
{
    private readonly IDamRepository _dams;
    private readonly IUnitOfWork _uow;

    public DeleteDamCommandHandler(IDamRepository dams, IUnitOfWork uow)
    {
        _dams = dams;
        _uow = uow;
    }

    public async Task<Unit> Handle(DeleteDamCommand request, CancellationToken ct)
    {
        var dam = await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id);
        // runs keep existing: the FK is set to null by the database
        _dams.Remove(dam);
        await _uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
