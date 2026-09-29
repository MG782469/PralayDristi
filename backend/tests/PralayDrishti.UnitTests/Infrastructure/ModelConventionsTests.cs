using Microsoft.EntityFrameworkCore;
using PralayDrishti.Domain.Entities;
using PralayDrishti.Infrastructure.Persistence;

namespace PralayDrishti.UnitTests.Infrastructure;

public class ModelConventionsTests
{
    // building the model needs the provider, not a live server
    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options);

    [Fact]
    public void Every_string_property_has_max_length_or_is_jsonb()
    {
        using var db = Context();
        var offenders = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (e, p)))
            .Where(x => x.p.ClrType == typeof(string))
            .Where(x => x.p.GetMaxLength() == null && x.p.GetColumnType() is not ("jsonb" or "text"))
            .Select(x => $"{x.e.ClrType.Name}.{x.p.Name}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Enums_are_stored_as_strings()
    {
        using var db = Context();
        var enums = db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum).ToList();
        Assert.NotEmpty(enums);
        Assert.All(enums, p => Assert.Equal(typeof(string), p.GetProviderClrType()));
    }

    [Fact]
    public void Email_index_is_unique()
    {
        using var db = Context();
        var user = db.Model.FindEntityType(typeof(User))!;
        var idx = user.GetIndexes().Single(i => i.Properties.Any(p => p.Name == nameof(User.NormalizedEmail)));
        Assert.True(idx.IsUnique);
    }

    [Fact]
    public void Dates_are_timestamptz()
    {
        using var db = Context();
        var dates = db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(DateTime)).ToList();
        Assert.All(dates, p => Assert.Equal("timestamp with time zone", p.GetColumnType()));
    }

    [Fact]
    public void Run_to_dam_is_set_null_on_delete()
    {
        using var db = Context();
        var fk = db.Model.FindEntityType(typeof(SimulationRun))!.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(Dam));
        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
    }
}
