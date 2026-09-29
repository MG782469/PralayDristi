using Microsoft.Extensions.Logging;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Jobs;

/// <summary>Daily: expired or revoked refresh tokens older than 30 days are only noise in the table.</summary>
public sealed class RefreshTokenCleanupJob
{
    private readonly IRefreshTokenRepository _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RefreshTokenCleanupJob> _log;

    public RefreshTokenCleanupJob(IRefreshTokenRepository tokens, IDateTimeProvider clock, ILogger<RefreshTokenCleanupJob> log)
    {
        _tokens = tokens;
        _clock = clock;
        _log = log;
    }

    public async Task Execute()
    {
        int n = await _tokens.DeleteStaleAsync(_clock.UtcNow.AddDays(-30));
        if (n > 0) _log.LogInformation("Removed {Count} stale refresh tokens", n);
    }
}
