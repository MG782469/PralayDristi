using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Lakes.GetWaterArea;

public sealed class GetWaterAreaQueryHandler : IRequestHandler<GetWaterAreaQuery, double>
{
    private readonly IFileStorage _files;
    private readonly IHydraulicAnalysis _analysis;

    public GetWaterAreaQueryHandler(IFileStorage files, IHydraulicAnalysis analysis)
    {
        _files = files;
        _analysis = analysis;
    }

    public async Task<double> Handle(GetWaterAreaQuery request, CancellationToken ct)
    {
        string path = await _files.SaveTempAsync(request.Ndwi, ".tif", ct);
        try
        {
            return await _analysis.WaterAreaKm2Async(path, request.Lon, request.Lat, request.Threshold, ct);
        }
        finally
        {
            _files.Delete(path);
        }
    }
}
