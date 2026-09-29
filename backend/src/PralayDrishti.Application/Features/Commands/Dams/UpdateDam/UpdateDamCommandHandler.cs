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

namespace PralayDrishti.Application.Features.Commands.Dams.UpdateDam;

public sealed class UpdateDamCommandHandler : IRequestHandler<UpdateDamCommand, DamDto>
{
    private readonly IDamRepository _dams;
    private readonly IUnitOfWork _uow;

    public UpdateDamCommandHandler(IDamRepository dams, IUnitOfWork uow)
    {
        _dams = dams;
        _uow = uow;
    }

    public async Task<DamDto> Handle(UpdateDamCommand request, CancellationToken ct)
    {
        var dam = await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id);
        dam.Apply(request.Dam);
        await _uow.SaveChangesAsync(ct);
        return dam.ToDto();
    }
}
