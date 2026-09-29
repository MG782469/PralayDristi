using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.UnitTests.Fakes;

public sealed class FakeClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class FakeUow : IUnitOfWork
{
    public int Saves { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
}

public sealed class FakeCurrentUser : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public UserRole? Role { get; set; }
    public string? IpAddress { get; set; } = "127.0.0.1";
}

public sealed class FakeUserRepo : IUserRepository
{
    public readonly List<User> Items = new();
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(u => u.Id == id));
    public Task AddAsync(User entity, CancellationToken ct = default) { Items.Add(entity); return Task.CompletedTask; }
    public void Remove(User entity) => Items.Remove(entity);
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(u => u.NormalizedEmail == normalizedEmail));
    public Task<bool> ExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(u => u.NormalizedEmail == normalizedEmail));
    public Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Items.ToList());
}

public sealed class FakeTokenRepo : IRefreshTokenRepository
{
    public readonly List<RefreshToken> Items = new();
    public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(t => t.Id == id));
    public Task AddAsync(RefreshToken entity, CancellationToken ct = default) { Items.Add(entity); return Task.CompletedTask; }
    public void Remove(RefreshToken entity) => Items.Remove(entity);
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t => t.TokenHash == tokenHash));
    public Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(t => t.UserId == userId && t.IsActive(nowUtc)).ToList());
    public Task<int> DeleteStaleAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
        Task.FromResult(Items.RemoveAll(t => t.ExpiresUtc < olderThanUtc));
}

/// <summary>Deterministic hasher; the real one is tested separately.</summary>
public sealed class FakeHasher : IPasswordHasher
{
    public string Hash(User user, string password) => "H(" + password + ")";
    public bool Verify(User user, string hash, string password) => hash == "H(" + password + ")";
}
