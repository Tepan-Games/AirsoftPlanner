using AirsoftPlanner.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Data;

/// <param name="Added">Éléments présents seulement dans l'autre copie, ajoutés.</param>
/// <param name="Updated">Éléments plus récents dans l'autre copie, mis à jour.</param>
/// <param name="Deleted">Éléments supprimés dans l'autre copie (après leur dernière modification ici).</param>
/// <param name="KeptLocal">Éléments plus récents dans ce fichier, conservés tels quels.</param>
public record MergeReport(int Added, int Updated, int Deleted, int KeptLocal)
{
    public int Changes => Added + Updated + Deleted;
}

/// <summary>
/// Fusionne dans un fichier d'OP une autre copie de la même OP (modifiée en parallèle par un autre orga).
/// Chaque élément est identifié par son Guid : ajouté s'il manque, remplacé si l'autre version est plus
/// récente (dernière modification), suppressions comprises.
/// </summary>
public static class OperationMerger
{
    public static MergeReport Merge(OperationFile target, string otherPath)
    {
        if (string.Equals(Path.GetFullPath(target.Path), Path.GetFullPath(otherPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choisissez une autre copie que le fichier ouvert.");

        using var other = OperationFile.Open(otherPath);
        var targetOperation = target.Context.Operations.IgnoreQueryFilters().AsNoTracking().Single();
        var otherOperation = other.Context.Operations.IgnoreQueryFilters().AsNoTracking().Single();
        if (targetOperation.Id != otherOperation.Id)
            throw new InvalidDataException(
                $"« {Path.GetFileName(otherPath)} » concerne une autre OP ({otherOperation.Name}) : seules les copies d'une même OP peuvent être fusionnées.");

        var report = new MergeReport(0, 0, 0, 0);
        var mergeSet = typeof(OperationMerger).GetMethod(nameof(MergeSet), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        foreach (var entityType in target.Context.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
            report = (MergeReport)mergeSet.MakeGenericMethod(entityType.ClrType).Invoke(null, [target.Context, other.Context, report])!;

        // Les dates de modification d'origine sont conservées : une fusion ultérieure reste correcte.
        target.Context.PreserveTimestamps = true;
        try
        {
            target.Context.SaveChanges();
        }
        finally
        {
            target.Context.PreserveTimestamps = false;
        }

        return report;
    }

    private static MergeReport MergeSet<T>(OperationDbContext target, OperationDbContext other, MergeReport report) where T : Entity
    {
        var local = target.Set<T>().IgnoreQueryFilters().ToDictionary(e => e.Id);
        foreach (var incoming in other.Set<T>().IgnoreQueryFilters().AsNoTracking().ToList())
        {
            if (!local.TryGetValue(incoming.Id, out var existing))
            {
                target.Add(incoming);
                report = report with { Added = report.Added + 1 };
            }
            else if (incoming.UpdatedAt > existing.UpdatedAt)
            {
                var deleted = incoming.IsDeleted && !existing.IsDeleted;
                target.Entry(existing).CurrentValues.SetValues(incoming);
                report = deleted ? report with { Deleted = report.Deleted + 1 } : report with { Updated = report.Updated + 1 };
            }
            else if (incoming.UpdatedAt < existing.UpdatedAt)
            {
                report = report with { KeptLocal = report.KeptLocal + 1 };
            }
        }

        return report;
    }
}
