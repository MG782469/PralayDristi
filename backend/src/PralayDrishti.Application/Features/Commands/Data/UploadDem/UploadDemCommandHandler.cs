using PralayDrishti.Application.Common.Models;
using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Features.Commands.Data.UploadDem;

public sealed class UploadDemCommandHandler : IRequestHandler<UploadDemCommand, string>
{
    private readonly IDemProvider _dem;

    public UploadDemCommandHandler(IDemProvider dem) => _dem = dem;

    public Task<string> Handle(UploadDemCommand request, CancellationToken ct) => _dem.SaveUploadAsync(request.Content, request.FileName, ct);
}
