using PralayDrishti.Application.Common.Models;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Queries.Wms.GetWms;

/// <summary>OGC WMS GetCapabilities / GetMap for Bhuvan, NDEM, QGIS.</summary>
public sealed record GetWmsQuery(IReadOnlyDictionary<string, string> Parameters, string BaseUrl) : IRequest<WmsResponse>;
