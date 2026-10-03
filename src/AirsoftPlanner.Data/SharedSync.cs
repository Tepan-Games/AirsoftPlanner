using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AirsoftPlanner.Data;

/// <param name="Received">Modifications des autres orgas intégrées à la copie locale.</param>
/// <param name="ConflictCopiesMerged">Copies en conflit créées par le service de synchronisation, fusionnées puis supprimées.</param>
/// <param name="Published">Vrai si le fichier partagé a été (re)publié.</param>
public record SyncReport(MergeReport Received, int ConflictCopiesMerged, bool Published);

/// <summary>
/// Travail à plusieurs sur une même OP via un dossier synchronisé (OneDrive, Google Drive, Dropbox, partage réseau).
/// Chaque orga travaille sur sa copie locale ; la synchronisation fusionne le fichier partagé dans la copie locale
/// (élément par élément, le plus récent l'emporte) puis republie le résultat. Le fichier partagé n'est jamais
/// ouvert directement : il est lu par copie et remplacé d'un bloc, pour éviter tout fichier à moitié écrit.
/// </summary>
public static class SharedSync
{
    public static SyncReport Sync(OperationFile local, string sharedPath)
    {
        local.Save();
        var folder = Path.GetDirectoryName(Path.GetFullPath(sharedPath))!;
        Directory.CreateDirectory(folder);

        var received = new MergeReport(0, 0, 0, 0);
        if (File.Exists(sharedPath))
            received = MergeCopyOf(local, sharedPath);

        // Copies en conflit laissées par le service de synchronisation (« OP-PC-de-Paul.aop », « OP (1).aop »...).
        var conflicts = ConflictCopies(sharedPath).ToList();
        foreach (var conflict in conflicts)
        {
            var report = MergeCopyOf(local, conflict);
            received = new MergeReport(received.Added + report.Added, received.Updated + report.Updated,
                received.Deleted + report.Deleted, received.KeptLocal + report.KeptLocal);
        }

        Publish(local, sharedPath);
        foreach (var conflict in conflicts)
            TryDelete(conflict);

        return new SyncReport(received, conflicts.Count, Published: true);
    }

    /// <summary>Fichiers du même dossier qui sont des copies en conflit du fichier partagé (même OP).</summary>
    public static IEnumerable<string> ConflictCopies(string sharedPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sharedPath))!;
        var name = Path.GetFileNameWithoutExtension(sharedPath);
        if (!Directory.Exists(folder))
            yield break;

        foreach (var candidate in Directory.EnumerateFiles(folder, name + "*" + OperationFile.Extension))
        {
            if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(sharedPath), StringComparison.OrdinalIgnoreCase))
                continue;
            if (SameOperation(candidate, sharedPath))
                yield return candidate;
        }
    }

    private static MergeReport MergeCopyOf(OperationFile local, string path)
    {
        // Lecture sur une copie temporaire : le service de synchronisation peut écrire le fichier au même moment.
        var temp = Path.Combine(Path.GetTempPath(), $"airsoft-planner-sync-{Guid.NewGuid():N}{OperationFile.Extension}");
        try
        {
            CopyWithRetry(path, temp);
            return OperationMerger.Merge(local, temp);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temp);
        }
    }

    private static void Publish(OperationFile local, string sharedPath)
    {
        local.Save();
        SqliteConnection.ClearAllPools();
        // Écrit à côté puis remplace d'un bloc : jamais de fichier partagé à moitié copié.
        var temp = sharedPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        CopyWithRetry(local.Path, temp);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, sharedPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(300 * attempt); // fichier momentanément verrouillé par le service de synchronisation
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
    }

    private static bool SameOperation(string a, string b)
    {
        try
        {
            using var first = OperationFile.Open(a);
            using var second = OperationFile.Open(b);
            return first.Context.Operations.IgnoreQueryFilters().Single().Id == second.Context.Operations.IgnoreQueryFilters().Single().Id;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private static void CopyWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(300 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Sera retenté à la prochaine synchronisation.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
