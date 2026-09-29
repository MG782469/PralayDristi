using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.Application.Common.Mappings;
using PralayDrishti.Application.Common.Models;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Exceptions;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Application.Services;

/// <summary>
/// Token pairs and refresh-token rotation.
/// Rotation: every refresh revokes the presented token and issues a successor. Presenting an already-revoked token
/// is treated as theft and revokes the whole family, except inside a short grace window after rotation - two tabs
/// refreshing at the same moment must not log the user out.
/// </summary>
public sealed class AuthService
{
    public static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(30);

    private readonly IRefreshTokenRepository _tokens;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ITokenService _tokenService;
    private readonly ICacheService _cache;
    private readonly IDateTimeProvider _clock;

    public AuthService(IRefreshTokenRepository tokens, IUserRepository users, IUnitOfWork uow, ITokenService tokenService,
        ICacheService cache, IDateTimeProvider clock)
    {
        _tokens = tokens;
        _users = users;
        _uow = uow;
        _tokenService = tokenService;
        _cache = cache;
        _clock = clock;
    }

    public async Task<AuthResult> IssueAsync(User user, string? ip, CancellationToken ct)
    {
        var (access, accessExp) = _tokenService.CreateAccessToken(user);
        var (raw, hash, exp) = _tokenService.CreateRefreshToken();
        await _tokens.AddAsync(RefreshToken.Issue(user.Id, hash, exp, _clock.UtcNow, ip), ct);
        await _uow.SaveChangesAsync(ct);
        return new AuthResult(access, accessExp, raw, exp, user.ToDto());
    }

    public async Task<AuthResult> RefreshAsync(string rawToken, string? ip, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        string hash = _tokenService.Hash(rawToken);
        var token = await _tokens.GetByHashAsync(hash, ct) ?? throw new ForbiddenAccessException("Invalid refresh token.");
        var user = await _users.GetByIdAsync(token.UserId, ct);
        if (user is null || !user.IsActive) throw new ForbiddenAccessException("Account is disabled.");

        if (token.IsRevoked)
        {
            if (token.RevokeReason == "rotated" && token.RevokedUtc is DateTime at && now - at <= GraceWindow)
            {
                string? successor = await _cache.GetAsync<string>(CacheKeys.RefreshGrace(hash), ct);
                var next = successor == null ? null : await _tokens.GetByHashAsync(_tokenService.Hash(successor), ct);
                if (successor != null && next != null && next.IsActive(now))
                {
                    var (acc, accExp) = _tokenService.CreateAccessToken(user);
                    return new AuthResult(acc, accExp, successor, next.ExpiresUtc, user.ToDto());
                }
            }

            foreach (var t in await _tokens.GetActiveForUserAsync(user.Id, now, ct))
                t.Revoke(now, ip, "reuse-detected");
            await _uow.SaveChangesAsync(ct);
            throw new ForbiddenAccessException("Refresh token reuse detected - please sign in again.");
        }

        if (!token.IsActive(now)) throw new ForbiddenAccessException("Refresh token expired.");

        var (access, accessExp) = _tokenService.CreateAccessToken(user);
        var (raw, newHash, exp) = _tokenService.CreateRefreshToken();
        token.Revoke(now, ip, "rotated", newHash);
        await _tokens.AddAsync(RefreshToken.Issue(user.Id, newHash, exp, now, ip), ct);
        await _uow.SaveChangesAsync(ct);
        await _cache.SetAsync(CacheKeys.RefreshGrace(hash), raw, GraceWindow, ct);
        return new AuthResult(access, accessExp, raw, exp, user.ToDto());
    }

    public async Task RevokeAsync(string rawToken, string? ip, CancellationToken ct)
    {
        var token = await _tokens.GetByHashAsync(_tokenService.Hash(rawToken), ct);
        if (token is null) return;   // logout is idempotent
        token.Revoke(_clock.UtcNow, ip, "logout");
        await _uow.SaveChangesAsync(ct);
    }

    public async Task RevokeAllAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        foreach (var t in await _tokens.GetActiveForUserAsync(userId, now, ct)) t.Revoke(now, null, reason);
        await _uow.SaveChangesAsync(ct);
    }
}
