using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Entities;
using AppHasher = PralayDrishti.Application.Common.Abstractions.IPasswordHasher;

namespace PralayDrishti.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "PralayDrishti";
    public string Audience { get; set; } = "PralayDrishti.Client";
    public string Secret { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public static class ClaimNames
{
    public const string UserId = "user_id";
    public const string Email = "email";
    public const string Role = "role";
}

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _opt;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _signing;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _opt = options.Value;
        _clock = clock;
        if (Encoding.UTF8.GetByteCount(_opt.Secret) < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 bytes (256 bits) for HMAC-SHA256.");
        _signing = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Secret)), SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTime ExpiresUtc) CreateAccessToken(User user)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_opt.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(ClaimNames.UserId, user.Id.ToString()),
            new Claim(ClaimNames.Email, user.Email),
            new Claim(ClaimNames.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(_opt.Issuer, _opt.Audience, claims, now, expires, _signing);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Raw, string Hash, DateTime ExpiresUtc) CreateRefreshToken()
    {
        string raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return (raw, Hash(raw), _clock.UtcNow.AddDays(_opt.RefreshTokenDays));
    }

    public string Hash(string rawRefreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken)));
}

public sealed class PasswordHasherAdapter : AppHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public bool Verify(User user, string hash, string password) =>
        !string.IsNullOrEmpty(hash) && _inner.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
}

public sealed class SystemClock : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
