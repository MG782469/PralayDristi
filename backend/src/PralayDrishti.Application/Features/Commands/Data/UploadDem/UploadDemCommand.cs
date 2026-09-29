using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadDem;

/// <summary>GeoTIFF DEM in EPSG:4326 (Copernicus GLO-30, FABDEM, SRTM, ALOS, CartoDEM...). Uploaded DEMs win over terrain tiles.</summary>
public sealed record UploadDemCommand(Stream Content, string FileName) : IRequest<string>;
