using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PralayDrishti.Infrastructure.Persistence.Configurations;

internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Opts = new();

    /// <summary>Stores a small collection as jsonb (polygons, cascade lists) with a comparer so EF sees changes.</summary>
    public static PropertyBuilder<T> AsJsonb<T>(this PropertyBuilder<T> p) where T : class?
    {
        p.HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, Opts),
                s => s == null ? default! : JsonSerializer.Deserialize<T>(s, Opts)!)
            .HasColumnType("jsonb");
        p.Metadata.SetValueComparer(new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, Opts) == JsonSerializer.Serialize(b, Opts),
            v => v == null ? 0 : JsonSerializer.Serialize(v, Opts).GetHashCode(),
            v => v == null ? v : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Opts), Opts)!));
        return p;
    }
}
