using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AirsoftPlanner.Data;

internal static class JsonListConversion
{
    /// <summary>Stocke une liste dans une colonne texte au format JSON.</summary>
    public static PropertyBuilder<List<T>> HasJsonListConversion<T>(this PropertyBuilder<List<T>> property) =>
        property.HasConversion(
            list => JsonSerializer.Serialize(list, JsonSerializerOptions.Default),
            json => JsonSerializer.Deserialize<List<T>>(json, JsonSerializerOptions.Default) ?? new List<T>(),
            new ValueComparer<List<T>>(
                (a, b) => a!.SequenceEqual(b!),
                list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                list => list.ToList()));
}
