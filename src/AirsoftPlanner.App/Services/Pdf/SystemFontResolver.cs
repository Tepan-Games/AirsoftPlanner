using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Fonts;

namespace AirsoftPlanner.App.Services.Pdf;

/// <summary>
/// Polices des PDF, lues dans les polices du système : Segoe UI, Consolas et Segoe UI Symbol sous Windows ;
/// DejaVu ou Liberation sous Linux. Les polices sont incorporées aux PDF.
/// </summary>
public sealed class SystemFontResolver : IFontResolver
{
    public const string Sans = "AP Sans";
    public const string SansSemiBold = "AP Sans SemiBold";
    public const string Mono = "AP Mono";
    public const string Symbol = "AP Symbol";

    // Visage → fichiers candidats (le premier trouvé est utilisé).
    private static readonly Dictionary<string, string[]> Faces = new()
    {
        ["sans"] = ["segoeui.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf"],
        ["sans-bold"] = ["segoeuib.ttf", "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf"],
        ["sans-italic"] = ["segoeuii.ttf", "DejaVuSans-Oblique.ttf", "LiberationSans-Italic.ttf"],
        ["sans-bolditalic"] = ["segoeuiz.ttf", "DejaVuSans-BoldOblique.ttf", "LiberationSans-BoldItalic.ttf"],
        ["sans-semibold"] = ["seguisb.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf"],
        ["mono"] = ["consola.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf"],
        ["mono-bold"] = ["consolab.ttf", "DejaVuSansMono-Bold.ttf", "LiberationMono-Bold.ttf"],
        ["symbol"] = ["seguisym.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf"],
    };

    private static readonly string[] Folders =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        "/usr/share/fonts", "/usr/local/share/fonts",
    ];

    private readonly Dictionary<string, byte[]> _cache = [];

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = familyName switch
        {
            Mono => bold ? "mono-bold" : "mono",
            Symbol => "symbol",
            SansSemiBold => "sans-semibold",
            _ => (bold, italic) switch
            {
                (true, true) => "sans-bolditalic",
                (true, false) => "sans-bold",
                (false, true) => "sans-italic",
                _ => "sans",
            },
        };
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(faceName, out var data))
                return data;
            var path = Faces[faceName].Select(Find).FirstOrDefault(p => p is not null)
                       ?? throw new FileNotFoundException($"Police introuvable pour les PDF : {string.Join(", ", Faces[faceName])}");
            return _cache[faceName] = File.ReadAllBytes(path);
        }
    }

    private static string? Find(string file)
    {
        foreach (var folder in Folders.Where(Directory.Exists))
        {
            var direct = Path.Combine(folder, file);
            if (File.Exists(direct))
                return direct;
            if (folder.StartsWith('/'))
            {
                var found = Directory.EnumerateFiles(folder, file, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null)
                    return found;
            }
        }
        return null;
    }
}
