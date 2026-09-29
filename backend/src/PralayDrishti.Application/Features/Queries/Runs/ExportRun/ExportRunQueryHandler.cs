using System.Text.Json;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Runs.ExportRun;

public sealed class ExportRunQueryHandler : IRequestHandler<ExportRunQuery, FileResultDto>
{
    private readonly ISimulationRunRepository _runs;
    private readonly IExportService _exports;

    public ExportRunQueryHandler(ISimulationRunRepository runs, IExportService exports)
    {
        _runs = runs;
        _exports = exports;
    }

    public async Task<FileResultDto> Handle(ExportRunQuery request, CancellationToken ct)
    {
        var run = await _runs.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("Run", request.Id);
        if (run.Status != RunStatus.Completed) throw new BusinessRuleException("The run has not completed yet.");
        return await _exports.ExportAsync(run.Id, request.Format.ToLowerInvariant(), ct);
    }
}
