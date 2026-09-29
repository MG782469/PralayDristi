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

namespace PralayDrishti.Application.Features.Commands.Dams.CreateDam;

public sealed class CreateDamCommandHandler : IRequestHandler<CreateDamCommand, DamDto>
{
    private readonly IDamRepository _dams;
    private readonly IUnitOfWork _uow;

    public CreateDamCommandHandler(IDamRepository dams, IUnitOfWork uow)
    {
        _dams = dams;
        _uow = uow;
    }

    public async Task<DamDto> Handle(CreateDamCommand request, CancellationToken ct)
    {
        if ((await _dams.ListAsync(ct)).Any(d => string.Equals(d.Name, request.Dam.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException($"A dam named '{request.Dam.Name}' already exists.");
        var dam = new Dam();
        dam.Apply(request.Dam);
        await _dams.AddAsync(dam, ct);
        await _uow.SaveChangesAsync(ct);
        return dam.ToDto();
    }
}
