namespace AirsoftPlanner.Core.Domain;

/// <summary>Nature d'une zone ou d'un point du terrain.</summary>
/// <remarks>Stocké en texte ; Other vient en premier (zones créées avant cette option).</remarks>
public enum PoiCategory
{
    Other,
    Objective,
    Bivouac,
    Camp,
    Respawn,
    Medical,
    Supply,
    Parking,
    Command,
    Danger,
}

/// <summary>Qui voit la zone ou le point sur son téléphone et dans son ordre de mission.</summary>
/// <remarks>Orga vient en premier : les zones existantes restent réservées à l'orga.</remarks>
public enum ZoneVisibility
{
    Orga,
    AllTeams,
    Faction,

    /// <summary>Seulement pendant une mission (dépôt d'armes, point de contact...) : visible par les équipes de la mission tant qu'elle leur est diffusée.</summary>
    DuringMission,
}

public static class PoiCategories
{
    public static IReadOnlyList<PoiCategory> All { get; } = Enum.GetValues<PoiCategory>();

    public static string Label(PoiCategory category) => category switch
    {
        PoiCategory.Objective => "Objectif",
        PoiCategory.Bivouac => "Bivouac",
        PoiCategory.Camp => "Campement / base",
        PoiCategory.Respawn => "Respawn",
        PoiCategory.Medical => "Infirmerie / zone neutre",
        PoiCategory.Supply => "Ravitaillement",
        PoiCategory.Parking => "Parking",
        PoiCategory.Command => "PC orga",
        PoiCategory.Danger => "Danger / zone interdite",
        _ => "Zone / point",
    };

    /// <summary>Symbole court affiché devant le nom (carte, téléphone).</summary>
    public static string Symbol(PoiCategory category) => category switch
    {
        PoiCategory.Objective => "◎",
        PoiCategory.Bivouac => "⛺",
        PoiCategory.Camp => "⚑",
        PoiCategory.Respawn => "↻",
        PoiCategory.Medical => "✚",
        PoiCategory.Supply => "▣",
        PoiCategory.Parking => "P",
        PoiCategory.Command => "★",
        PoiCategory.Danger => "⚠",
        _ => "",
    };
}

public static class ZoneVisibilityRules
{
    /// <summary>La zone est-elle communiquée à cette équipe (téléphone, ordre de mission) ?</summary>
    public static bool IsVisibleTo(this Zone zone, Team team) => zone.Visibility switch
    {
        ZoneVisibility.AllTeams => true,
        ZoneVisibility.Faction => zone.VisibleFactionId is not null && zone.VisibleFactionId == team.FactionId,
        ZoneVisibility.DuringMission => zone.VisibleMissionId is not null && zone.VisibleMissionId == team.PublishedMissionId,
        _ => false,
    };
}
