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

namespace PralayDrishti.Application.Features.Commands.Dams;

public sealed record CreateDamCommand(DamDto Dam) : IRequest<DamDto>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.Dams };
}

public sealed class CreateDamCommandValidator : AbstractValidator<CreateDamCommand>
{
    public CreateDamCommandValidator() => RuleFor(x => x.Dam).NotNull().SetValidator(new DamDtoValidator());
}

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

public sealed record UpdateDamCommand(int Id, DamDto Dam) : IRequest<DamDto>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[]
    {
        CacheKeys.Dams, CacheKeys.DamScenario(Id, false), CacheKeys.DamScenario(Id, true)
    };
}

public sealed class UpdateDamCommandValidator : AbstractValidator<UpdateDamCommand>
{
    public UpdateDamCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Dam).NotNull().SetValidator(new DamDtoValidator());
    }
}

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

public sealed record DeleteDamCommand(int Id) : IRequest<Unit>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { CacheKeys.Dams, CacheKeys.DamScenario(Id, false), CacheKeys.DamScenario(Id, true) };
}

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

public sealed record SimulateDamCommand(int Id, bool Cascade) : IRequest<RunSummaryDto>;

public sealed class SimulateDamCommandHandler : IRequestHandler<SimulateDamCommand, RunSummaryDto>
{
    private readonly IDamRepository _dams;
    private readonly IFloodSimulationEngine _engine;
    private readonly RunFactory _factory;

    public SimulateDamCommandHandler(IDamRepository dams, IFloodSimulationEngine engine, RunFactory factory)
    {
        _dams = dams;
        _engine = engine;
        _factory = factory;
    }

    public async Task<RunSummaryDto> Handle(SimulateDamCommand request, CancellationToken ct)
    {
        var dam = await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id);
        var spec = _engine.DefaultScenario(dam, await _dams.ListAsync(ct), request.Cascade);
        return (await _factory.QueueAsync(spec, RunOrigin.User, dam.Id, null, ct)).ToDto();
    }
}
