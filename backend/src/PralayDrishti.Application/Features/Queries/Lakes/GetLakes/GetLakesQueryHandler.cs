using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Lakes.GetLakes;

public sealed class GetLakesQueryHandler : IRequestHandler<GetLakesQuery, IReadOnlyList<LakeDto>>
{
    private readonly ILakeDetectionRepository _lakes;

    public GetLakesQueryHandler(ILakeDetectionRepository lakes) => _lakes = lakes;

    public async Task<IReadOnlyList<LakeDto>> Handle(GetLakesQuery request, CancellationToken ct) =>
        (await _lakes.ListAsync(Math.Clamp(request.Take, 1, 500), ct)).Select(l => l.ToDto()).ToList();
}
