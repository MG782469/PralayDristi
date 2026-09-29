using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams.GetDamById;

public sealed class GetDamByIdQueryHandler : IRequestHandler<GetDamByIdQuery, DamDto>
{
    private readonly IDamRepository _dams;

    public GetDamByIdQueryHandler(IDamRepository dams) => _dams = dams;

    public async Task<DamDto> Handle(GetDamByIdQuery request, CancellationToken ct) =>
        (await _dams.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Dam", request.Id)).ToDto();
}
