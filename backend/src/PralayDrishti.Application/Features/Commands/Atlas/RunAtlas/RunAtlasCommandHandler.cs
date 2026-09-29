using MediatR;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Services;

namespace PralayDrishti.Application.Features.Commands.Atlas.RunAtlas;

public sealed class RunAtlasCommandHandler : IRequestHandler<RunAtlasCommand, IReadOnlyList<Guid>>
{
    private readonly AtlasService _atlas;

    public RunAtlasCommandHandler(AtlasService atlas) => _atlas = atlas;

    public Task<IReadOnlyList<Guid>> Handle(RunAtlasCommand request, CancellationToken ct) => _atlas.QueueAsync(request.DamIds, ct);
}
