using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.ViewModels;

/// <summary>Fichier d'OP ouvert récemment : nom, dossier et date de dernière modification.</summary>
public record RecentFile(string Path, string Name, string Folder, string Modified, bool Exists)
{
    public static IEnumerable<RecentFile> From(IEnumerable<string> paths) => paths.Select(path =>
    {
        var exists = File.Exists(path);
        var modified = exists ? File.GetLastWriteTime(path).ToString("d MMM yyyy HH:mm", L.Culture) : L.T("introuvable");
        return new RecentFile(path, System.IO.Path.GetFileNameWithoutExtension(path), System.IO.Path.GetDirectoryName(path) ?? "", modified, exists);
    });
}
