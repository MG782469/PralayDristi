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

/// <summary>What gee/lake_watch.py posts (the same JSON it drops in data/gee_outbox).</summary>
public sealed record IngestDetectionsCommand(IReadOnlyList<LakeDetectionInput> Detections, bool? AutoSimulate) : IRequest<IReadOnlyList<Guid>>;
