using FluentValidation;
using MediatR;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Lakes.GetLakes;

public sealed record GetLakesQuery(int Take = 200) : IRequest<IReadOnlyList<LakeDto>>;
