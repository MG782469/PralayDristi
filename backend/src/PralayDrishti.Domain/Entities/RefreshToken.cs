namespace PralayDrishti.Domain.Entities;

public class RefreshToken
{
    private RefreshToken() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTime ExpiresUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public string? CreatedByIp { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public string? RevokedByIp { get; private set; }
    public string? RevokeReason { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, DateTime expiresUtc, DateTime nowUtc, string? ip) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        ExpiresUtc = expiresUtc,
        CreatedUtc = nowUtc,
        CreatedByIp = ip
    };

    public bool IsRevoked => RevokedUtc != null;
    public bool IsActive(DateTime nowUtc) => !IsRevoked && nowUtc < ExpiresUtc;

    public void Revoke(DateTime nowUtc, string? ip, string reason, string? replacedByHash = null)
    {
        if (IsRevoked) return;   // the first revocation carries the real reason
        RevokedUtc = nowUtc;
        RevokedByIp = ip;
        RevokeReason = reason;
        ReplacedByTokenHash = replacedByHash;
    }
}
