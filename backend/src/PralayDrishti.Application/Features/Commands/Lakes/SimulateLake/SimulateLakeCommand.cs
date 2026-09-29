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

namespace PralayDrishti.Application.Features.Commands.Lakes.SimulateLake;

public sealed record SimulateLakeCommand(Guid Id) : IRequest<Guid>;
