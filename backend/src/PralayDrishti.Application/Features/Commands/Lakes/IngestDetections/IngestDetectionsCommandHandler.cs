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

namespace PralayDrishti.Application.Features.Commands.Lakes.IngestDetections;

public sealed class IngestDetectionsCommandHandler : IRequestHandler<IngestDetectionsCommand, IReadOnlyList<Guid>>
{
    private readonly LakeToAlertService _chain;
    private readonly bool _autoDefault;

    public IngestDetectionsCommandHandler(LakeToAlertService chain, IOptions<LakeWatchOptions>? options = null)
    {
        _chain = chain;
        _autoDefault = options?.Value.AutoSimulate ?? true;
    }

    public Task<IReadOnlyList<Guid>> Handle(IngestDetectionsCommand request, CancellationToken ct) =>
        _chain.IngestAsync(request.Detections, request.AutoSimulate ?? _autoDefault, ct);
}
