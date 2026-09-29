using PralayDrishti.Domain.Entities;
using PralayDrishti.Domain.Enums;

namespace PralayDrishti.Domain.Interfaces;

public interface IRepository<T, in TKey> where T : class
{
    Task<T?> GetByIdAsync(TKey id, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Remove(T entity);
}

public interface IUserRepository : IRepository<User, Guid>
{
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<bool> ExistsAsync(string normalizedEmail, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken, Guid>
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);
    Task<int> DeleteStaleAsync(DateTime olderThanUtc, CancellationToken ct = default);
}

public interface IDamRepository : IRepository<Dam, int>
{
    Task<IReadOnlyList<Dam>> ListAsync(CancellationToken ct = default);
}

public interface ISimulationRunRepository : IRepository<SimulationRun, Guid>
{
    Task<IReadOnlyList<SimulationRun>> ListAsync(RunOrigin? origin, int take, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationRun>> ListByStatusAsync(IEnumerable<RunStatus> statuses, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationRun>> LatestCompletedPerDamAsync(RunOrigin origin, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface ILakeDetectionRepository : IRepository<LakeDetection, Guid>
{
    Task<IReadOnlyList<LakeDetection>> ListAsync(int take, CancellationToken ct = default);
    Task<LakeDetection?> GetByRunIdAsync(Guid runId, CancellationToken ct = default);
}

public interface IAlertRepository : IRepository<Alert, Guid>
{
    Task<IReadOnlyList<Alert>> ListAsync(int take, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
