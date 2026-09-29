using PralayDrishti.Application.Common.Abstractions;
using PralayDrishti.Infrastructure.BackgroundJobs;

namespace PralayDrishti.Infrastructure.Services;

public sealed class EnvironmentInfo : IEnvironmentInfo
{
    private readonly AppOptions _opt;
    private readonly RedisStatus _redis;

    public EnvironmentInfo(AppOptions opt, RedisStatus redis)
    {
        _opt = opt;
        _redis = redis;
    }

    public bool Offline => _opt.Offline;
    public bool OsmEnabled => _opt.UseOsm && !_opt.Offline;
    public bool Delft3DConfigured => !string.IsNullOrWhiteSpace(_opt.Delft3DCommand);
    public bool RedisConnected => _redis.Connected;
}
