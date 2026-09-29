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

namespace PralayDrishti.Application.Features.Queries.Runs.GetLayerImage;

public sealed class GetLayerImageQueryHandler : IRequestHandler<GetLayerImageQuery, byte[]>
{
    private readonly IMapRenderer _renderer;

    public GetLayerImageQueryHandler(IMapRenderer renderer) => _renderer = renderer;

    public Task<byte[]> Handle(GetLayerImageQuery request, CancellationToken ct) =>
        Task.FromResult(_renderer.RenderLayerPng(request.Id, request.Layer) ?? throw new NotFoundException($"Layer {request.Layer} of run", request.Id));
}
