using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Dams.GetDams;

public sealed class GetDamsQueryHandler : IRequestHandler<GetDamsQuery, IReadOnlyList<DamDto>>
{
    private readonly IDamRepository _dams;

    public GetDamsQueryHandler(IDamRepository dams) => _dams = dams;

    public async Task<IReadOnlyList<DamDto>> Handle(GetDamsQuery request, CancellationToken ct) =>
        (await _dams.ListAsync(ct)).Select(d => d.ToDto()).ToList();
}
