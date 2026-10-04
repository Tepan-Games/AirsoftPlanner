using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.App.Services.Pdf;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.App.Services;

/// <summary>Tout ce qu'il faut pour produire le package d'une équipe.</summary>
public record PackageInput(
    Operation Operation,
    Team Team,
    IReadOnlyList<TeamMember> Members,
    Faction? Faction,
    Team? CommandTeam,
    TeamMember? CommandLeader,
    IReadOnlyList<Mission> Missions,
    IReadOnlyDictionary<Guid, Mission> AllMissions,
    IReadOnlyDictionary<Guid, Zone> Zones,
    IReadOnlyDictionary<Guid, GameItem> Items,
    IReadOnlyList<RuleDocument> Rules,
    MapLayer? Map,
    string? EnrollmentServer = null,
    IReadOnlyDictionary<Guid, string>? FactionColors = null);

/// <summary>
/// Produit le package d'une équipe : un dossier contenant l'ordre de mission initial (PDF, avec carte),
/// les règles rédigées (PDF) et les règles importées (fichiers d'origine), plus une archive ZIP à envoyer.
/// </summary>
public static class PackageGenerator
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    private static readonly TextStyle Strong = new(SemiBold: true);

    /// <returns>Chemin du dossier du package (l'archive ZIP porte le même nom).</returns>
    public static string Generate(PackageInput input, string outputRoot)
    {
        var name = SafeFileName($"{input.Operation.Name} - {input.Team.Name}");
        var folder = Path.Combine(outputRoot, name);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        MissionOrder(input).Save(Path.Combine(folder, SafeFileName(L.F("ordre_de_mission_x_pdf", input.Team.Name))));

        var index = 1;
        foreach (var rule in input.Rules)
        {
            var prefix = L.F("regles_x_x", index++, rule.Title);
            if (rule.IsImported)
                File.WriteAllBytes(Path.Combine(folder, SafeFileName(prefix + Path.GetExtension(rule.FileName))), rule.FileContent);
            else
                RulesDocument(input.Operation, rule).Save(Path.Combine(folder, SafeFileName(prefix + ".pdf")));
        }

        var zip = folder + ".zip";
        if (File.Exists(zip))
            File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip);
        return folder;
    }

    /// <summary>Met en page des règles rédigées dans le logiciel.</summary>
    public static void WriteRulesPdf(Operation op, RuleDocument rule, string path) => RulesDocument(op, rule).Save(path);

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "package" : cleaned;
    }

    // ----- Ordre de mission -----

    private static PdfDoc MissionOrder(PackageInput input)
    {
        var op = input.Operation;
        var format = op.CoordinateFormat;
        var factionColor = input.Faction?.Color ?? "#607D8B";
        var day = op.StartsAt.LocalDateTime.Date;
        var leader = input.Members.FirstOrDefault(m => m.IsLeader);

        var document = new PdfDoc(10);
        var col = document.Section(header: ("ORDRE DE MISSION", op.Name, factionColor, 2), footer: FooterText());
        col.Spacing = 10;

        col.Line((input.Team.Name, new TextStyle(20, Bold: true)),
            (input.Faction is { } f ? $"   {f.Name}" : "", new TextStyle(14, SemiBold: true, Color: factionColor)));

        col.Columns([1, 1], 10,
            c => c.Box(L.T("identification"), b =>
            {
                b.Line((L.T("brassard_2"), Strong), (ArmbandText(input.Faction), default));
                b.Line((L.T("tenue"), Strong), (Blank(input.Faction?.Uniform, "libre"), default));
                b.Line((L.T("chef_d_equipe"), Strong), (leader is null ? L.T("non_designe") : $"{MemberName(leader)} {leader.Phone}", default));
            }),
            c => c.Box(L.T("transmissions"), b =>
            {
                b.Line((L.T("frequence_equipe"), Strong), (Blank(input.Team.RadioFrequency, L.T("a_definir")), default));
                b.Line((L.T("frequence_faction"), Strong), (Blank(input.Faction?.RadioFrequency, L.T("a_definir")), default));
                var command = input.CommandTeam is null ? L.T("non_designe")
                    : input.CommandTeam.Id == input.Team.Id ? L.T("votre_equipe")
                    : $"{input.CommandTeam.Name}{(input.CommandLeader is { } cl ? $" — {MemberName(cl)} {cl.Phone}" : "")}";
                b.Line((L.T("chef_de_faction"), Strong), (command, default));
                if (op.OrgaRadioFrequency.Length > 0)
                    b.Line((L.T("frequence_orga"), Strong), (op.OrgaRadioFrequency, default));
                if (op.EmergencyPhone.Length > 0)
                    b.Line((L.T("urgence_orga"), new TextStyle(SemiBold: true, Color: PdfColors.Red)), (op.EmergencyPhone, new TextStyle(Bold: true)));
            }));

        col.Box(L.T("operation"), b =>
        {
            b.Text(L.F("du_x_au_x", op.StartsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French), op.EndsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French)));
            if (op.OrganizerName.Length > 0)
                b.Line((L.T("organise_par"), Strong), (op.OrganizerName, default));
            if (op.Location.Length > 0)
                b.Line((L.T("lieu"), Strong), (op.Location, default));
            if (op.RuleSet == AirsoftPlanner.Core.Documents.GameRuleSet.Acp)
                b.Line((L.T("reglement_de_jeu") + " : ", Strong), (L.T("reglement_acp"), default));
            if (op.Description.Length > 0)
                b.Text(op.Description, spaceBefore: 4);
        });

        if (input.Members.Count > 0)
            col.Box(L.F("effectif_x", input.Members.Count), b => b.Table([3, 2, 2, 2],
                [L.T("nom"), L.T("indicatif"), L.T("role"), L.T("portable")],
                input.Members.Select(member => (IReadOnlyList<(string, TextStyle)>)
                [
                    ($"{member.FirstName} {member.LastName}{(member.IsLeader ? L.T("chef_2") : "")}", default),
                    (member.Callsign, default), (member.Role, default), (member.Phone, default),
                ]), rowPadding: 1, headerLine: null));

        if (input.EnrollmentServer is { Length: > 0 } server && input.Team.EnrollmentCode.Length > 0)
            EnrollmentBlock(col, server, input.Team.EnrollmentCode);

        var points = input.Zones.Values.Where(z => z.Points.Count > 0 && z.Visibility != ZoneVisibility.DuringMission && z.IsVisibleTo(input.Team))
            .OrderBy(z => z.Category).ThenBy(z => z.Name, StringComparer.CurrentCulture).ToList();
        if (points.Count > 0)
            col.Box(L.T("points_d_interet"), b =>
            {
                foreach (var point in points)
                    b.Line(($"{PoiCategories.Label(point.Category)} : ", Strong), (point.Name, default),
                        ($" — {Coordinates.Format(point.Kind == ZoneKind.Area ? GeoMath.Centroid(point.Points) : point.Points[0], format)}", default),
                        (point.Description.Length > 0 ? $" · {point.Description}" : "", new TextStyle(Color: PdfColors.GreyDarken2)));
            });

        col.Text("Missions", new TextStyle(14, Bold: true), spaceBefore: 6);
        if (input.Missions.Count == 0)
            col.Text(L.T("aucune_mission_assignee_pour_le_moment"), new TextStyle(Italic: true));
        foreach (var mission in input.Missions)
            MissionBlock(col, mission, input, format, day);

        if (input.Map is { } map)
        {
            var missionZones = input.Missions.Select(m => m.ZoneId).OfType<Guid>().ToHashSet();
            // Les points réservés à l'orga ou à une autre faction (bivouac adverse...) ne figurent pas sur la carte de l'équipe.
            var shown = input.Zones.Values.Where(z => missionZones.Contains(z.Id)
                || (z.Visibility != ZoneVisibility.DuringMission && z.IsVisibleTo(input.Team))); // points de mission : secrets jusqu'à la mission
            var image = MapSnapshot.Render(map, shown.Select(z => (z, missionZones.Contains(z.Id))),
                factionColor: id => input.FactionColors?.GetValueOrDefault(id));
            var page = document.Section(landscape: true, marginCm: 1, header: ($"CARTE — {input.Team.Name}", op.Name, factionColor, 2),
                footer: FooterText(), footerLines: [L.F("zones_de_vos_missions_en_trait_epais_x", map.Attribution)]);
            page.Image(image, alignment: MigraDoc.DocumentObjectModel.ParagraphAlignment.Center);
        }

        return document;
    }

    /// <summary>QR code et code pour enrôler le téléphone du chef d'équipe dans l'application Android.</summary>
    private static void EnrollmentBlock(PdfFlow col, string server, string code)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(Core.Gps.EnrollmentLink.Create(server, code), QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        col.Box(L.T("application_android_suivi_de_l_equipe"), b => b.Columns([3.2, b.WidthCm - 3.2], 10,
            q => q.Image(png, widthCm: 3.2),
            t =>
            {
                t.Spacing = 2;
                t.Text(L.T("chef_d_equipe_installez_l_application_airsoft_pl"));
                t.Line((L.T("ou_saisissez_serveur"), Strong), (server, new TextStyle(Mono: true)));
                t.Line((L.T("code_d_equipe"), Strong), (Core.Gps.EnrollmentCodes.Format(code), new TextStyle(14, Bold: true, Mono: true)));
                t.Text(L.T("le_jour_de_l_op_connectez_vous_au_wi_fi_du_terra"), new TextStyle(8, Color: PdfColors.GreyDarken1));
            }));
    }

    private static void MissionBlock(PdfFlow col, Mission mission, PackageInput input, CoordinateFormat format, DateTime day)
    {
        input.Zones.TryGetValue(mission.ZoneId ?? Guid.Empty, out var zone);
        col.Box(null, b =>
        {
            b.Line(($"{day.AddMinutes(mission.StartMinutes).ToString("ddd d HH:mm", French)} – {day.AddMinutes(mission.EndMinutes):HH:mm}   ",
                    new TextStyle(SemiBold: true, Color: PdfColors.GreyDarken3)),
                (mission.Name, new TextStyle(12, Bold: true)),
                (mission.IsEssential ? "" : L.T("optionnelle_2"), new TextStyle(Italic: true, Color: PdfColors.GreyDarken1)));

            if (zone is not null)
                b.Line((L.T("zone_3"), Strong), (zone.Name, default),
                    (zone.Points.Count > 0 ? $" — {(zone.Kind == ZoneKind.Area ? "centre " : "")}{Coordinates.Format(GeoMath.Centroid(zone.Points), format)}" : "", default));

            var partners = mission.TeamIds.Count > 1 ? mission.TeamIds.Count - 1 : 0;
            if (partners > 0)
                b.Text(L.F("mission_conjointe_avec_x_autre_s_equipe_s", partners), new TextStyle(Color: PdfColors.GreyDarken2));

            var predecessors = mission.PredecessorIds.Select(id => input.AllMissions.GetValueOrDefault(id)?.Name).OfType<string>().ToList();
            if (predecessors.Count > 0)
                b.Line((L.T("apres"), Strong), (string.Join(", ", predecessors), default));

            var items = mission.Items.Select(u => input.Items.TryGetValue(u.ItemId, out var item) ? $"{u.Quantity} × {item.Name}" : null).OfType<string>().ToList();
            if (items.Count > 0)
                b.Line((L.T("materiel"), Strong), (string.Join(", ", items), default));

            if (mission.Description.Length > 0)
                b.Text(mission.Description, spaceBefore: 3);
        }, background: PdfColors.White, border: PdfColors.GreyLighten1, spacing: 3);
    }

    // ----- Règles rédigées -----

    private static PdfDoc RulesDocument(Operation op, RuleDocument rule)
    {
        var document = new PdfDoc(10.5);
        var col = document.Section(marginCm: 1.8, header: ("RÈGLES DU JEU", op.Name, "#37474F", 2), footer: FooterText());
        col.Spacing = 5;
        col.Text(rule.Title, new TextStyle(18, Bold: true));
        foreach (var line in rule.Text.Replace("\r", "").Split('\n'))
        {
            if (line.StartsWith("# "))
                col.Text(line[2..], new TextStyle(13, Bold: true), spaceBefore: 8);
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                col.Bullet(line[2..]);
            else if (line.Trim().Length > 0)
                col.Text(line);
        }
        return document;
    }

    // ----- Éléments communs -----

    private static string FooterText() => L.F("genere_le_x_avec_airsoft_planner_page", DateTime.Now.ToString("d MMMM yyyy HH:mm", French));

    private static string MemberName(TeamMember member)
    {
        var name = $"{member.FirstName} {member.LastName}".Trim();
        return member.Callsign.Length == 0 ? name : $"{name} « {member.Callsign} »";
    }

    private static string ArmbandText(Faction? faction) => faction is null || faction.ArmbandColor.Length == 0
        ? "aucun"
        : ColorName(faction.ArmbandColor);

    private static string ColorName(string hex) => hex.ToUpperInvariant() switch
    {
        "#C62828" => "rouge",
        "#1565C0" => "bleu",
        "#2E7D32" => "vert",
        "#F9A825" => "jaune",
        "#EF6C00" => "orange",
        "#6A1B9A" => "violet",
        "#00838F" => "cyan",
        "#AD1457" => "rose",
        "#4E342E" => "marron",
        "#546E7A" => "gris",
        "#212121" => "noir",
        "#FAFAFA" => "blanc",
        _ => hex,
    };

    private static string Blank(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
