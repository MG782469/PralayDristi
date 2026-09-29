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

namespace PralayDrishti.Application.Features.Commands.Lakes.DetectFromNdwi;

public sealed class DetectFromNdwiCommandHandler : IRequestHandler<DetectFromNdwiCommand, IReadOnlyList<Guid>>
{
    private readonly IFileStorage _files;
    private readonly IHydraulicAnalysis _analysis;
    private readonly LakeToAlertService _chain;

    public DetectFromNdwiCommandHandler(IFileStorage files, IHydraulicAnalysis analysis, LakeToAlertService chain)
    {
        _files = files;
        _analysis = analysis;
        _chain = chain;
    }

    public async Task<IReadOnlyList<Guid>> Handle(DetectFromNdwiCommand request, CancellationToken ct)
    {
        string before = await _files.SaveTempAsync(request.Before, ".tif", ct);
        string after = await _files.SaveTempAsync(request.After, ".tif", ct);
        try
        {
            var found = await _analysis.DetectNewLakesAsync(before, after, request.Threshold, request.MinAreaKm2, ct);
            return found.Count == 0 ? Array.Empty<Guid>() : await _chain.IngestAsync(found, request.AutoSimulate, ct);
        }
        finally
        {
            _files.Delete(before);
            _files.Delete(after);
        }
    }
}
