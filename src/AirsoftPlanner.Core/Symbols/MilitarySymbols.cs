using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Core.Symbols;

/// <summary>
/// Symboles militaires (inspirés de l'APP-6 / MIL-STD-2525, cadre « ami » rectangulaire) affichés aux couleurs des factions.
/// </summary>
/// <remarks>Stocké en texte ; Auto vient en premier (symbole déduit de la catégorie, zones existantes).</remarks>
public enum MilSymbol
{
    Auto,
    Dot,
    Infantry,
    MechanizedInfantry,
    Armour,
    Reconnaissance,
    Headquarters,
    Logistics,
    Medical,
    Engineer,
    Signal,
    Artillery,
    WheeledVehicle,
    Helicopter,
    Installation,
    Bivouac,
    ObservationPost,
    Objective,
    RallyPoint,
    Checkpoint,
    Danger,
    LandingZone,
}

/// <summary>Indicateur de taille de l'unité, dessiné au-dessus du cadre.</summary>
public enum Echelon
{
    None,
    Team,
    Squad,
    Section,
    Platoon,
    Company,
}

/// <summary>Élément de dessin, en coordonnées relatives au cadre (0..1 en largeur et en hauteur ; hors cadre au-delà).</summary>
public abstract record SymbolShape;

public record SymbolLine(float X1, float Y1, float X2, float Y2) : SymbolShape;

public record SymbolEllipse(float X, float Y, float RadiusX, float RadiusY, bool Filled) : SymbolShape;

public record SymbolPolygon(IReadOnlyList<(float X, float Y)> Points, bool Filled, bool Closed = true) : SymbolShape;

/// <summary>Texte centré sur (X, Y), hauteur relative à celle du cadre.</summary>
public record SymbolText(float X, float Y, string Text, float Size) : SymbolShape;

public enum SymbolFrame
{
    /// <summary>Cadre rectangulaire (unité, équipement, installation), rempli de la couleur de la faction.</summary>
    Rectangle,

    /// <summary>Cercle (point tactique : objectif, rassemblement...).</summary>
    Circle,

    /// <summary>Triangle (poste d'observation, danger).</summary>
    Triangle,

    /// <summary>Simple point (pas de symbole militaire).</summary>
    Dot,
}

/// <summary>Symbole prêt à dessiner : cadre, pictogramme et indicateurs (taille, QG, installation).</summary>
public record SymbolDrawing(SymbolFrame Frame, IReadOnlyList<SymbolShape> Shapes)
{
    /// <summary>Rapport largeur / hauteur du cadre.</summary>
    public float Aspect => Frame == SymbolFrame.Rectangle ? 1.5f : 1f;
}

public static class MilitarySymbols
{
    /// <summary>Symboles proposés dans les listes (Auto en premier).</summary>
    public static IReadOnlyList<MilSymbol> All { get; } = Enum.GetValues<MilSymbol>();

    public static string Label(MilSymbol symbol) => symbol switch
    {
        MilSymbol.Auto => L.T("automatique_selon_la_categorie"),
        MilSymbol.Dot => L.T("simple_point"),
        MilSymbol.Infantry => L.T("infanterie"),
        MilSymbol.MechanizedInfantry => L.T("infanterie_mecanisee"),
        MilSymbol.Armour => L.T("blinde"),
        MilSymbol.Reconnaissance => L.T("reconnaissance"),
        MilSymbol.Headquarters => L.T("qg_commandement"),
        MilSymbol.Logistics => L.T("logistique_ravitaillement"),
        MilSymbol.Medical => L.T("sante_infirmerie"),
        MilSymbol.Engineer => L.T("genie"),
        MilSymbol.Signal => L.T("transmissions"),
        MilSymbol.Artillery => L.T("appui_feu_artillerie"),
        MilSymbol.WheeledVehicle => L.T("vehicule_a_roues"),
        MilSymbol.Helicopter => L.T("helicoptere"),
        MilSymbol.Installation => L.T("installation_campement"),
        MilSymbol.Bivouac => L.T("bivouac"),
        MilSymbol.ObservationPost => L.T("poste_d_observation"),
        MilSymbol.Objective => L.T("objectif"),
        MilSymbol.RallyPoint => L.T("point_de_rassemblement_respawn"),
        MilSymbol.Checkpoint => L.T("point_de_controle"),
        MilSymbol.Danger => L.T("danger_zone_minee"),
        MilSymbol.LandingZone => L.T("zone_de_poser_helicoptere"),
        _ => symbol.ToString(),
    };

    public static string Label(Echelon echelon) => echelon switch
    {
        Echelon.Team => L.T("equipe_3"),
        Echelon.Squad => L.T("groupe"),
        Echelon.Section => L.T("section"),
        Echelon.Platoon => L.T("peloton"),
        Echelon.Company => L.T("compagnie_i"),
        _ => L.T("aucun"),
    };

    /// <summary>Symbole retenu pour une catégorie de point quand le symbole est « Automatique ».</summary>
    public static MilSymbol Resolve(MilSymbol symbol, PoiCategory category) => symbol != MilSymbol.Auto ? symbol : category switch
    {
        PoiCategory.Objective => MilSymbol.Objective,
        PoiCategory.Bivouac => MilSymbol.Bivouac,
        PoiCategory.Camp => MilSymbol.Installation,
        PoiCategory.Respawn => MilSymbol.RallyPoint,
        PoiCategory.Medical => MilSymbol.Medical,
        PoiCategory.Supply => MilSymbol.Logistics,
        PoiCategory.Parking => MilSymbol.WheeledVehicle,
        PoiCategory.Command => MilSymbol.Headquarters,
        PoiCategory.Danger => MilSymbol.Danger,
        _ => MilSymbol.Dot,
    };

    /// <summary>Taille d'une équipe de joueurs → indicateur de taille.</summary>
    public static Echelon EchelonForSize(int players) => players switch
    {
        <= 0 => Echelon.None,
        <= 4 => Echelon.Team,
        <= 10 => Echelon.Squad,
        <= 20 => Echelon.Section,
        <= 40 => Echelon.Platoon,
        _ => Echelon.Company,
    };

    public static SymbolDrawing Draw(MilSymbol symbol, Echelon echelon = Echelon.None)
    {
        var shapes = new List<SymbolShape>();
        var frame = SymbolFrame.Rectangle;
        switch (symbol)
        {
            case MilSymbol.Auto or MilSymbol.Dot:
                return new SymbolDrawing(SymbolFrame.Dot, []);
            case MilSymbol.Infantry:
                shapes.AddRange(Cross());
                break;
            case MilSymbol.MechanizedInfantry:
                shapes.AddRange(Cross());
                shapes.Add(new SymbolEllipse(0.5f, 0.5f, 0.3f, 0.22f, false));
                break;
            case MilSymbol.Armour:
                shapes.Add(new SymbolEllipse(0.5f, 0.5f, 0.3f, 0.22f, false));
                break;
            case MilSymbol.Reconnaissance:
                shapes.Add(new SymbolLine(0, 1, 1, 0));
                break;
            case MilSymbol.Headquarters:
                // Hampe du QG : trait descendant depuis le coin inférieur gauche.
                shapes.Add(new SymbolLine(0, 1, 0, 1.7f));
                shapes.Add(new SymbolText(0.5f, 0.5f, "QG", 0.5f));
                break;
            case MilSymbol.Logistics:
                shapes.Add(new SymbolLine(0, 0.68f, 1, 0.68f));
                break;
            case MilSymbol.Medical:
                shapes.Add(new SymbolLine(0.5f, 0, 0.5f, 1));
                shapes.Add(new SymbolLine(0, 0.5f, 1, 0.5f));
                break;
            case MilSymbol.Engineer:
                shapes.Add(new SymbolPolygon([(0.28f, 0.68f), (0.28f, 0.36f), (0.72f, 0.36f), (0.72f, 0.68f)], false, Closed: false));
                shapes.Add(new SymbolLine(0.5f, 0.36f, 0.5f, 0.68f));
                break;
            case MilSymbol.Signal:
                shapes.Add(new SymbolPolygon([(0, 0), (0.45f, 0.62f), (0.55f, 0.38f), (1, 1)], false, Closed: false));
                break;
            case MilSymbol.Artillery:
                shapes.Add(new SymbolEllipse(0.5f, 0.5f, 0.09f, 0.135f, true));
                break;
            case MilSymbol.WheeledVehicle:
                shapes.Add(new SymbolPolygon([(0.25f, 0.3f), (0.75f, 0.3f), (0.75f, 0.6f), (0.25f, 0.6f)], false));
                shapes.Add(new SymbolEllipse(0.35f, 0.74f, 0.055f, 0.08f, false));
                shapes.Add(new SymbolEllipse(0.65f, 0.74f, 0.055f, 0.08f, false));
                break;
            case MilSymbol.Helicopter:
                shapes.Add(new SymbolPolygon([(0.22f, 0.3f), (0.78f, 0.7f), (0.78f, 0.3f), (0.22f, 0.7f)], true));
                break;
            case MilSymbol.Installation:
                shapes.Add(new SymbolPolygon([(0.38f, -0.16f), (0.62f, -0.16f), (0.62f, 0), (0.38f, 0)], true));
                break;
            case MilSymbol.Bivouac:
                shapes.Add(new SymbolPolygon([(0.38f, -0.16f), (0.62f, -0.16f), (0.62f, 0), (0.38f, 0)], true));
                shapes.Add(new SymbolPolygon([(0.28f, 0.78f), (0.5f, 0.25f), (0.72f, 0.78f)], false));
                break;
            case MilSymbol.ObservationPost:
                frame = SymbolFrame.Triangle;
                shapes.Add(new SymbolEllipse(0.5f, 0.66f, 0.08f, 0.08f, true));
                break;
            case MilSymbol.Danger:
                frame = SymbolFrame.Triangle;
                shapes.Add(new SymbolText(0.5f, 0.66f, "!", 0.55f));
                break;
            case MilSymbol.Objective:
                frame = SymbolFrame.Circle;
                shapes.Add(new SymbolText(0.5f, 0.5f, "OBJ", 0.36f));
                break;
            case MilSymbol.RallyPoint:
                frame = SymbolFrame.Circle;
                shapes.Add(new SymbolText(0.5f, 0.5f, "RP", 0.42f));
                break;
            case MilSymbol.Checkpoint:
                frame = SymbolFrame.Circle;
                shapes.Add(new SymbolText(0.5f, 0.5f, "CP", 0.42f));
                break;
            case MilSymbol.LandingZone:
                frame = SymbolFrame.Circle;
                shapes.Add(new SymbolText(0.5f, 0.5f, "H", 0.55f));
                break;
        }

        shapes.AddRange(EchelonMarks(echelon));
        return new SymbolDrawing(frame, shapes);
    }

    private static SymbolShape[] Cross() => [new SymbolLine(0, 0, 1, 1), new SymbolLine(0, 1, 1, 0)];

    // Au-dessus du cadre : Ø équipe, points pour groupe / section / peloton, barre pour la compagnie.
    private static IEnumerable<SymbolShape> EchelonMarks(Echelon echelon)
    {
        const float y = -0.2f;
        switch (echelon)
        {
            case Echelon.Team:
                yield return new SymbolEllipse(0.5f, y, 0.06f, 0.09f, false);
                yield return new SymbolLine(0.42f, y + 0.12f, 0.58f, y - 0.12f);
                break;
            case Echelon.Squad or Echelon.Section or Echelon.Platoon:
                var count = echelon == Echelon.Squad ? 1 : echelon == Echelon.Section ? 2 : 3;
                for (var i = 0; i < count; i++)
                    yield return new SymbolEllipse(0.5f + (i - (count - 1) / 2f) * 0.12f, y, 0.035f, 0.05f, true);
                break;
            case Echelon.Company:
                yield return new SymbolLine(0.5f, y - 0.1f, 0.5f, y + 0.1f);
                break;
        }
    }

    /// <summary>Couleur de remplissage : couleur de la faction éclaircie, pour que le pictogramme noir reste lisible.</summary>
    public static (byte R, byte G, byte B) FillColor(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return ((byte)((r + 255 * 1.2) / 2.2), (byte)((g + 255 * 1.2) / 2.2), (byte)((b + 255 * 1.2) / 2.2));
    }

    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 8)
            text = text[2..];
        return text.Length == 6 && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value)
            ? ((byte)(value >> 16), (byte)(value >> 8 & 0xFF), (byte)(value & 0xFF))
            : ((byte)0x60, (byte)0x7D, (byte)0x8B);
    }
}
