using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadExposure;

public sealed class UploadExposureCommandHandler : IRequestHandler<UploadExposureCommand, UploadExposureResult>
{
    private readonly IExposureProvider _exposure;

    public UploadExposureCommandHandler(IExposureProvider exposure) => _exposure = exposure;

    public async Task<UploadExposureResult> Handle(UploadExposureCommand request, CancellationToken ct)
    {
        var (points, roads, buildings) = await _exposure.SaveUploadAsync(request.GeoJson, request.FileName, ct);
        return new UploadExposureResult(points, roads, buildings);
    }
}
