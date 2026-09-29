using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Domain.Entities;

public class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = "";
    public string NormalizedEmail { get; private set; } = "";
    public string FullName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? LastLoginUtc { get; private set; }

    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public static User Create(string email, string fullName, string passwordHash, UserRole role, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        Email = email.Trim(),
        NormalizedEmail = Normalize(email),
        FullName = fullName.Trim(),
        PasswordHash = passwordHash,
        Role = role,
        IsActive = true,
        CreatedUtc = nowUtc
    };

    // the ASP.NET Core hasher wants the user instance, so the hash is set right after creation
    public void SetPasswordHash(string hash) => PasswordHash = hash;
    public void ChangeRole(UserRole role) => Role = role;
    public void SetActive(bool active) => IsActive = active;
    public void MarkLogin(DateTime nowUtc) => LastLoginUtc = nowUtc;
}
