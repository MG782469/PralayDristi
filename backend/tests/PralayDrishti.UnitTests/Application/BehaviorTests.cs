using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using PralayDrishti.Application.Common.Behaviors;
using PralayDrishti.Application.Common.Caching;
using PralayDrishti.UnitTests.Fakes;
using ValidationException = PralayDrishti.Application.Common.Exceptions.ValidationException;

namespace PralayDrishti.UnitTests.Application;

public record Ping(string Name, int Age) : IRequest<string>;

public class PingValidator : AbstractValidator<Ping>
{
    public PingValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Age).InclusiveBetween(0, 120);
    }
}

public record CachedPing(string Key) : IRequest<string>, ICacheableQuery
{
    public string CacheKey => Key;
    public TimeSpan Expiry => TimeSpan.FromMinutes(5);
}

public record InvalidatingPing(string Key) : IRequest<string>, ICacheInvalidator
{
    public IEnumerable<string> InvalidatesKeys => new[] { Key };
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Throws_with_all_failures_when_invalid()
    {
        var behavior = new ValidationBehavior<Ping, string>(new[] { new PingValidator() });
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(new Ping("", 500), () => Task.FromResult("pong"), default));
        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains("Name", ex.Errors.Keys);
    }

    [Fact]
    public async Task Calls_next_when_valid()
    {
        var behavior = new ValidationBehavior<Ping, string>(new[] { new PingValidator() });
        var r = await behavior.Handle(new Ping("a", 3), () => Task.FromResult("pong"), default);
        Assert.Equal("pong", r);
    }
}

public class CachingBehaviorTests
{
    [Fact]
    public async Task Returns_cached_value_without_calling_handler()
    {
        var cache = new FakeCache();
        await cache.SetAsync("k", "cached", TimeSpan.FromMinutes(1));
        var behavior = new CachingBehavior<CachedPing, string>(cache, NullLogger<CachingBehavior<CachedPing, string>>.Instance);
        bool called = false;
        var r = await behavior.Handle(new CachedPing("k"), () => { called = true; return Task.FromResult("fresh"); }, default);
        Assert.Equal("cached", r);
        Assert.False(called);
    }

    [Fact]
    public async Task Stores_result_on_miss()
    {
        var cache = new FakeCache();
        var behavior = new CachingBehavior<CachedPing, string>(cache, NullLogger<CachingBehavior<CachedPing, string>>.Instance);
        var r = await behavior.Handle(new CachedPing("k2"), () => Task.FromResult("fresh"), default);
        Assert.Equal("fresh", r);
        Assert.Equal("fresh", await cache.GetAsync<string>("k2"));
    }

    [Fact]
    public async Task Invalidator_removes_keys_after_success()
    {
        var cache = new FakeCache();
        await cache.SetAsync("dams:all", "x", TimeSpan.FromMinutes(1));
        var behavior = new CachingBehavior<InvalidatingPing, string>(cache, NullLogger<CachingBehavior<InvalidatingPing, string>>.Instance);
        await behavior.Handle(new InvalidatingPing("dams:all"), () => Task.FromResult("ok"), default);
        Assert.Null(await cache.GetAsync<string>("dams:all"));
    }
}
