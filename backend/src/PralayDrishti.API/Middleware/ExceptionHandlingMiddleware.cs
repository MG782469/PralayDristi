using Microsoft.AspNetCore.Mvc;
using PralayDrishti.Domain.Exceptions;
using ValidationException = PralayDrishti.Application.Common.Exceptions.ValidationException;

namespace PralayDrishti.API.Middleware;

/// <summary>Domain / validation exceptions -> RFC 7807 problem details. No stack traces outside Development.</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log, IHostEnvironment env)
    {
        _next = next;
        _log = log;
        _env = env;
    }

    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // client went away - nothing to answer
        }
        catch (Exception ex)
        {
            if (ctx.Response.HasStarted) throw;
            var (status, title) = ex switch
            {
                ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
                NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
                BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Request cannot be processed"),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
            };
            if (status == 500) _log.LogError(ex, "Unhandled exception on {Path}", ctx.Request.Path);

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == 500 && !_env.IsDevelopment() ? "Something went wrong. It has been logged." : ex.Message,
                Instance = ctx.Request.Path
            };
            if (ex is ValidationException ve) problem.Extensions["errors"] = ve.Errors;
            // login failures are 401, not 403: the caller isn't authenticated yet
            if (ex is ForbiddenAccessException && ctx.Request.Path.StartsWithSegments("/api/auth")) problem.Status = status = 401;

            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        }
    }
}
