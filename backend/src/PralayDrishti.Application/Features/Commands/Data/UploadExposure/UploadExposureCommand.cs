using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadExposure;

/// <summary>Settlements / critical facilities / building footprints as GeoJSON.</summary>
public sealed record UploadExposureCommand(string GeoJson, string FileName) : IRequest<UploadExposureResult>;
