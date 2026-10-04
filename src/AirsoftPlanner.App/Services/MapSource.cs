using System.Collections.Generic;
using System.Globalization;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>Un service de tuiles téléchargeable (grille Web Mercator).</summary>
public record MapSource(string Name, string Attribution, int MaxZoom, string UrlTemplate)
{
    public string TileUrl(int zoom, int x, int y) => UrlTemplate
        .Replace("{z}", zoom.ToString(CultureInfo.InvariantCulture))
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => Name;

    private const string IgnWmts =
        "https://data.geopf.fr/wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&STYLE=normal" +
        "&TILEMATRIXSET=PM&TILEMATRIX={z}&TILECOL={x}&TILEROW={y}";

    /// <summary>Fonds de la Géoplateforme IGN, libres d'utilisation (Licence Ouverte Etalab), sans clé d'accès.</summary>
    public static IReadOnlyList<MapSource> All { get; } =
    [
        new(L.T("photo_aerienne_ign"), L.T("ign_orthophotos"), 19,
            IgnWmts + "&LAYER=ORTHOIMAGERY.ORTHOPHOTOS&FORMAT=image/jpeg"),
        new(L.T("plan_ign"), L.T("ign_plan_ign_v2"), 19,
            IgnWmts + "&LAYER=GEOGRAPHICALGRIDSYSTEMS.PLANIGNV2&FORMAT=image/png"),
    ];
}
