using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Revoked_token_is_not_active()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now, "1.2.3.4");
        Assert.True(t.IsActive(Now));
        t.Revoke(Now.AddMinutes(1), "1.2.3.4", "logout");
        Assert.False(t.IsActive(Now.AddMinutes(2)));
        Assert.Equal("logout", t.RevokeReason);
    }

    [Fact]
    public void Expired_token_is_not_active()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now, null);
        Assert.False(t.IsActive(Now.AddDays(7).AddSeconds(1)));
    }

    [Fact]
    public void Revoking_twice_keeps_first_reason()
    {
        var t = RefreshToken.Issue(Guid.NewGuid(), "hash", Now.AddDays(7), Now, null);
        t.Revoke(Now, null, "rotated", "next");
        t.Revoke(Now.AddMinutes(1), null, "reuse");
        Assert.Equal("rotated", t.RevokeReason);
        Assert.Equal("next", t.ReplacedByTokenHash);
    }
}

public class UserTests
{
    [Fact]
    public void Create_normalizes_email()
    {
        var u = User.Create(" Admin@X.com ", "Admin", "h", UserRole.Admin, DateTime.UtcNow);
        Assert.Equal("Admin@X.com", u.Email);
        Assert.Equal("ADMIN@X.COM", u.NormalizedEmail);
        Assert.True(u.IsActive);
    }

    [Fact]
    public void Normalize_is_static_and_shared()
    {
        Assert.Equal("A@B.C", User.Normalize("  a@b.c "));
    }
}
