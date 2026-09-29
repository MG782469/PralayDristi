using Microsoft.EntityFrameworkCore;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;
using PralayDrishti.Domain.Interfaces;

namespace PralayDrishti.Infrastructure.Persistence.Repositories;

public abstract class Repository<T, TKey> : IRepository<T, TKey> where T : class
{
    protected readonly AppDbContext Db;

    protected Repository(AppDbContext db) => Db = db;

    public virtual async Task<T?> GetByIdAsync(TKey id, CancellationToken ct = default) =>
        await Db.Set<T>().FindAsync(new object?[] { id }, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) => await Db.Set<T>().AddAsync(entity, ct);

    public void Remove(T entity) => Db.Set<T>().Remove(entity);
}

public sealed class UserRepository : Repository<User, Guid>, IUserRepository
{
    public UserRepository(AppDbContext db) : base(db) { }

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        Db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public Task<bool> ExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        Db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default) =>
        await Db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
}

public sealed class RefreshTokenRepository : Repository<RefreshToken, Guid>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext db) : base(db) { }

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        Db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default) =>
        await Db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedUtc == null && t.ExpiresUtc > nowUtc).ToListAsync(ct);

    public Task<int> DeleteStaleAsync(DateTime olderThanUtc, CancellationToken ct = default) =>
        Db.RefreshTokens.Where(t => t.ExpiresUtc < olderThanUtc || (t.RevokedUtc != null && t.RevokedUtc < olderThanUtc))
            .ExecuteDeleteAsync(ct);
}

public sealed class DamRepository : Repository<Dam, int>, IDamRepository
{
    public DamRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Dam>> ListAsync(CancellationToken ct = default) =>
        await Db.Dams.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
}

public sealed class SimulationRunRepository : Repository<SimulationRun, Guid>, ISimulationRunRepository
{
    public SimulationRunRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<SimulationRun>> ListAsync(RunOrigin? origin, int take, CancellationToken ct = default)
    {
        var q = Db.Runs.AsNoTracking();
        if (origin is RunOrigin o) q = q.Where(r => r.Origin == o);
        return await q.OrderByDescending(r => r.CreatedUtc).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationRun>> ListByStatusAsync(IEnumerable<RunStatus> statuses, CancellationToken ct = default)
    {
        var set = statuses.ToList();
        return await Db.Runs.Where(r => set.Contains(r.Status)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationRun>> LatestCompletedPerDamAsync(RunOrigin origin, CancellationToken ct = default)
    {
        var runs = await Db.Runs.AsNoTracking()
            .Where(r => r.Origin == origin && r.Status == RunStatus.Completed && r.DamId != null)
            .OrderByDescending(r => r.FinishedUtc)
            .ToListAsync(ct);
        return runs.GroupBy(r => r.DamId).Select(g => g.First()).ToList();
    }

    public Task<int> CountAsync(CancellationToken ct = default) => Db.Runs.CountAsync(ct);
}

public sealed class LakeDetectionRepository : Repository<LakeDetection, Guid>, ILakeDetectionRepository
{
    public LakeDetectionRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<LakeDetection>> ListAsync(int take, CancellationToken ct = default) =>
        await Db.Lakes.AsNoTracking().OrderByDescending(l => l.DetectedUtc).Take(take).ToListAsync(ct);

    public Task<LakeDetection?> GetByRunIdAsync(Guid runId, CancellationToken ct = default) =>
        Db.Lakes.FirstOrDefaultAsync(l => l.RunId == runId, ct);
}

public sealed class AlertRepository : Repository<Alert, Guid>, IAlertRepository
{
    public AlertRepository(AppDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Alert>> ListAsync(int take, CancellationToken ct = default) =>
        await Db.Alerts.AsNoTracking().OrderByDescending(a => a.CreatedUtc).Take(take).ToListAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => Db.Alerts.CountAsync(ct);
}
