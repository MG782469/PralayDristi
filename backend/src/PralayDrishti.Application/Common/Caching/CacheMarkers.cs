namespace PralayDrishti.Application.Common.Caching;

/// <summary>A query whose result can be served from the cache.</summary>
public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan Expiry { get; }
    bool BypassCache => false;
}

/// <summary>A command that makes cached entries stale once it succeeds.</summary>
public interface ICacheInvalidator
{
    IEnumerable<string> InvalidatesKeys { get; }
}

public static class CacheKeys
{
    public const string Dams = "dams:all";
    public const string SystemInfo = "system:info";
    public const string Leaderboard = "atlas:leaderboard";
    public const string AnalyticChecks = "validation:scoreboard:analytic";

    public static string DamScenario(int id, bool cascade) => $"dams:{id}:scenario:{cascade}";
    public static string RunDetail(Guid id) => $"runs:{id:N}:detail";
    public static string RunProfile(Guid id) => $"runs:{id:N}:profile";
    public static string RunCancel(Guid id) => $"runs:{id:N}:cancel";
    public static string RefreshGrace(string oldHash) => $"rt-grace:{oldHash}";
}
