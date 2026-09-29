using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlertCap;

/// <summary>The CAP 1.2 document, ready for NDMA SACHET or any CAP aggregator.</summary>
public sealed record GetAlertCapQuery(Guid Id) : IRequest<string>;
