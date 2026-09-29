using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;

namespace PralayDrishti.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _log;
    private readonly ICurrentUserService _user;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> log, ICurrentUserService user)
    {
        _log = log;
        _user = user;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await next();
        }
        finally
        {
            _log.LogInformation("{Request} by {User} took {Ms} ms", typeof(TRequest).Name, _user.UserId?.ToString() ?? "anonymous",
                sw.ElapsedMilliseconds);
        }
    }
}

/// <summary>Marker for requests that are expected to be slow (model re-runs), so they don't spam warnings.</summary>
public interface ILongRunningRequest { }

public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _log;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TRequest, TResponse>> log) => _log = log;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        if (sw.ElapsedMilliseconds > 2000 && request is not ILongRunningRequest)
            _log.LogWarning("Slow request {Request}: {Ms} ms", typeof(TRequest).Name, sw.ElapsedMilliseconds);
        return response;
    }
}
