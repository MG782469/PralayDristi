using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Lakes.GetWaterArea;

/// <summary>Water area of the lake around a point in one NDWI raster - input for the satellite reservoir estimate.</summary>
public sealed record GetWaterAreaQuery(Stream Ndwi, double Lon, double Lat, double Threshold) : IRequest<double>;
