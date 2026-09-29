using System.Text;
using MediatR;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Features.Queries.Alerts.GetAlertFeed;

/// <summary>Atom feed of the latest alerts - the usual way CAP aggregators poll.</summary>
public sealed record GetAlertFeedQuery(string BaseUrl) : IRequest<string>;
