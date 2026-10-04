using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

    static PackageGenerator()
    {
        // Licence Community : gratuite pour les particuliers, associations et structures de moins d'1 M$ de CA annuel.
        QuestPDF.Settings.License = LicenseType.Community;
        // Polices Windows en secours : symboles et emojis saisis dans les noms (★, ⛺...).
        QuestPDF.Settings.UseSystemFonts = true;
    }

    /// <returns>Chemin du dossier du package (l'archive ZIP porte le même nom).</returns>
    public static string Generate(PackageInput input, string outputRoot)
    {
        var name = SafeFileName($"{input.Operation.Name} - {input.Team.Name}");
        var folder = Path.Combine(outputRoot, name);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        MissionOrder(input).GeneratePdf(Path.Combine(folder, SafeFileName(L.F("ordre_de_mission_x_pdf", input.Team.Name))));

        var index = 1;
        foreach (var rule in input.Rules)
        {
            var prefix = L.F("regles_x_x", index++, rule.Title);
            if (rule.IsImported)
                File.WriteAllBytes(Path.Combine(folder, SafeFileName(prefix + Path.GetExtension(rule.FileName))), rule.FileContent);
            else
                RulesDocument(input.Operation, rule).GeneratePdf(Path.Combine(folder, SafeFileName(prefix + ".pdf")));
        }

        var zip = folder + ".zip";
        if (File.Exists(zip))
            File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip);
        return folder;
    }

    /// <summary>Met en page des règles rédigées dans le logiciel.</summary>
    public static void WriteRulesPdf(Operation op, RuleDocument rule, string path) => RulesDocument(op, rule).GeneratePdf(path);

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "package" : cleaned;
    }

    // ----- Ordre de mission -----

    private static IDocument MissionOrder(PackageInput input)
    {
        var op = input.Operation;
        var format = op.CoordinateFormat;
        var factionColor = input.Faction?.Color ?? "#607D8B";
        var day = op.StartsAt.LocalDateTime.Date;
        var leader = input.Members.FirstOrDefault(m => m.IsLeader);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));
                page.Header().Element(h => Header(h, op, "ORDRE DE MISSION", factionColor));
                page.Footer().Element(Footer);

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Text(text =>
                    {
                        text.Span(input.Team.Name).FontSize(20).Bold();
                        if (input.Faction is { } faction)
                            text.Span($"   {faction.Name}").FontSize(14).FontColor(factionColor).SemiBold();
                    });

                    col.Item().Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem().Element(c => Box(c, L.T("identification"), b =>
                        {
                            b.Item().Text(t => { t.Span(L.T("brassard_2")).SemiBold(); t.Span(ArmbandText(input.Faction)); });
                            b.Item().Text(t => { t.Span(L.T("tenue")).SemiBold(); t.Span(Blank(input.Faction?.Uniform, "libre")); });
                            b.Item().Text(t => { t.Span(L.T("chef_d_equipe")).SemiBold(); t.Span(leader is null ? L.T("non_designe") : $"{MemberName(leader)} {leader.Phone}"); });
                        }));
                        row.RelativeItem().Element(c => Box(c, L.T("transmissions"), b =>
                        {
                            b.Item().Text(t => { t.Span(L.T("frequence_equipe")).SemiBold(); t.Span(Blank(input.Team.RadioFrequency, L.T("a_definir"))); });
                            b.Item().Text(t => { t.Span(L.T("frequence_faction")).SemiBold(); t.Span(Blank(input.Faction?.RadioFrequency, L.T("a_definir"))); });
                            var command = input.CommandTeam is null ? L.T("non_designe")
                                : input.CommandTeam.Id == input.Team.Id ? L.T("votre_equipe")
                                : $"{input.CommandTeam.Name}{(input.CommandLeader is { } cl ? $" — {MemberName(cl)} {cl.Phone}" : "")}";
                            b.Item().Text(t => { t.Span(L.T("chef_de_faction")).SemiBold(); t.Span(command); });
                            if (op.OrgaRadioFrequency.Length > 0)
                                b.Item().Text(t => { t.Span(L.T("frequence_orga")).SemiBold(); t.Span(op.OrgaRadioFrequency); });
                            if (op.EmergencyPhone.Length > 0)
                                b.Item().Text(t => { t.Span(L.T("urgence_orga")).SemiBold().FontColor(Colors.Red.Darken2); t.Span(op.EmergencyPhone).Bold(); });
                        }));
                    });

                    col.Item().Element(c => Box(c, L.T("operation"), b =>
                    {
                        b.Item().Text(L.F("du_x_au_x", op.StartsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French), op.EndsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French)));
                        if (op.OrganizerName.Length > 0)
                            b.Item().Text(t => { t.Span(L.T("organise_par")).SemiBold(); t.Span(op.OrganizerName); });
                        if (op.Location.Length > 0)
                            b.Item().Text(t => { t.Span(L.T("lieu")).SemiBold(); t.Span(op.Location); });
                        if (op.Description.Length > 0)
                            b.Item().PaddingTop(4).Text(op.Description);
                    }));

                    if (input.Members.Count > 0)
                        col.Item().Element(c => Box(c, L.F("effectif_x", input.Members.Count), b => b.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); cols.RelativeColumn(2); cols.RelativeColumn(2); });
                            table.Header(h =>
                            {
                                foreach (var header in new[] { L.T("nom"), L.T("indicatif"), L.T("role"), L.T("portable") })
                                    h.Cell().Text(header).SemiBold();
                            });
                            foreach (var member in input.Members)
                            {
                                table.Cell().Text($"{member.FirstName} {member.LastName}{(member.IsLeader ? L.T("chef_2") : "")}");
                                table.Cell().Text(member.Callsign);
                                table.Cell().Text(member.Role);
                                table.Cell().Text(member.Phone);
                            }
                        })));

                    if (input.EnrollmentServer is { Length: > 0 } server && input.Team.EnrollmentCode.Length > 0)
                        col.Item().Element(c => EnrollmentBlock(c, server, input.Team.EnrollmentCode));

                    var points = input.Zones.Values.Where(z => z.Points.Count > 0 && z.Visibility != ZoneVisibility.DuringMission && z.IsVisibleTo(input.Team))
                        .OrderBy(z => z.Category).ThenBy(z => z.Name, StringComparer.CurrentCulture).ToList();
                    if (points.Count > 0)
                        col.Item().Element(c => Box(c, L.T("points_d_interet"), b =>
                        {
                            foreach (var point in points)
                                b.Item().Text(t =>
                                {
                                    t.Span($"{PoiCategories.Label(point.Category)} : ").SemiBold();
                                    t.Span(point.Name);
                                    t.Span($" — {Coordinates.Format(point.Kind == ZoneKind.Area ? GeoMath.Centroid(point.Points) : point.Points[0], format)}");
                                    if (point.Description.Length > 0)
                                        t.Span($" · {point.Description}").FontColor(Colors.Grey.Darken2);
                                });
                        }));

                    col.Item().PaddingTop(6).Text("Missions").FontSize(14).Bold();
                    if (input.Missions.Count == 0)
                        col.Item().Text(L.T("aucune_mission_assignee_pour_le_moment")).Italic();

                    foreach (var mission in input.Missions)
                        col.Item().Element(c => MissionBlock(c, mission, input, format, day));
                });
            });

            if (input.Map is { } map)
            {
                var missionZones = input.Missions.Select(m => m.ZoneId).OfType<Guid>().ToHashSet();
                // Les points réservés à l'orga ou à une autre faction (bivouac adverse...) ne figurent pas sur la carte de l'équipe.
                var shown = input.Zones.Values.Where(z => missionZones.Contains(z.Id)
                    || (z.Visibility != ZoneVisibility.DuringMission && z.IsVisibleTo(input.Team))); // points de mission : secrets jusqu'à la mission
                var image = MapSnapshot.Render(map, shown.Select(z => (z, missionZones.Contains(z.Id))),
                    factionColor: id => input.FactionColors?.GetValueOrDefault(id));
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, Unit.Centimetre);
                    page.Header().Element(h => Header(h, op, $"CARTE — {input.Team.Name}", factionColor));
                    page.Footer().Column(col =>
                    {
                        col.Item().Text(L.F("zones_de_vos_missions_en_trait_epais_x", map.Attribution)).FontSize(8).FontColor(Colors.Grey.Darken1);
                        col.Item().Element(Footer);
                    });
                    page.Content().PaddingVertical(6).AlignCenter().Image(image).FitArea();
                });
            }
        });
    }

    /// <summary>QR code et code pour enrôler le téléphone du chef d'équipe dans l'application Android.</summary>
    private static void EnrollmentBlock(IContainer container, string server, string code)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(Core.Gps.EnrollmentLink.Create(server, code), QRCoder.QRCodeGenerator.ECCLevel.M);
        var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
        container.Element(c => Box(c, L.T("application_android_suivi_de_l_equipe"), b => b.Item().Row(row =>
        {
            row.ConstantItem(3.2f, Unit.Centimetre).Image(png);
            row.RelativeItem().PaddingLeft(10).Column(text =>
            {
                text.Spacing(2);
                text.Item().Text(L.T("chef_d_equipe_installez_l_application_airsoft_pl"));
                text.Item().Text(t => { t.Span(L.T("ou_saisissez_serveur")).SemiBold(); t.Span(server).FontFamily("Consolas"); });
                text.Item().Text(t => { t.Span(L.T("code_d_equipe")).SemiBold(); t.Span(Core.Gps.EnrollmentCodes.Format(code)).FontSize(14).Bold().FontFamily("Consolas"); });
                text.Item().Text(L.T("le_jour_de_l_op_connectez_vous_au_wi_fi_du_terra"))
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        })));
    }

    private static void MissionBlock(IContainer container, Mission mission, PackageInput input, CoordinateFormat format, DateTime day)
    {
        input.Zones.TryGetValue(mission.ZoneId ?? Guid.Empty, out var zone);
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(col =>
        {
            col.Spacing(3);
            col.Item().Row(row =>
            {
                row.AutoItem().Text($"{day.AddMinutes(mission.StartMinutes).ToString("ddd d HH:mm", French)} – {day.AddMinutes(mission.EndMinutes):HH:mm}")
                    .SemiBold().FontColor(Colors.Grey.Darken3);
                row.RelativeItem().PaddingLeft(10).Text(t =>
                {
                    t.Span(mission.Name).Bold().FontSize(12);
                    if (!mission.IsEssential)
                        t.Span(L.T("optionnelle_2")).Italic().FontColor(Colors.Grey.Darken1);
                });
            });

            if (zone is not null)
                col.Item().Text(t =>
                {
                    t.Span(L.T("zone_3")).SemiBold();
                    t.Span(zone.Name);
                    if (zone.Points.Count > 0)
                        t.Span($" — {(zone.Kind == ZoneKind.Area ? "centre " : "")}{Coordinates.Format(GeoMath.Centroid(zone.Points), format)}");
                });

            var partners = mission.TeamIds.Count > 1 ? mission.TeamIds.Count - 1 : 0;
            if (partners > 0)
                col.Item().Text(L.F("mission_conjointe_avec_x_autre_s_equipe_s", partners)).FontColor(Colors.Grey.Darken2);

            var predecessors = mission.PredecessorIds.Select(id => input.AllMissions.GetValueOrDefault(id)?.Name).OfType<string>().ToList();
            if (predecessors.Count > 0)
                col.Item().Text(t => { t.Span(L.T("apres")).SemiBold(); t.Span(string.Join(", ", predecessors)); });

            var items = mission.Items.Select(u => input.Items.TryGetValue(u.ItemId, out var item) ? $"{u.Quantity} × {item.Name}" : null).OfType<string>().ToList();
            if (items.Count > 0)
                col.Item().Text(t => { t.Span(L.T("materiel")).SemiBold(); t.Span(string.Join(", ", items)); });

            if (mission.Description.Length > 0)
                col.Item().PaddingTop(3).Text(mission.Description);
        });
    }

    // ----- Règles rédigées -----

    private static IDocument RulesDocument(Operation op, RuleDocument rule) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.8f, Unit.Centimetre);
            page.DefaultTextStyle(t => t.FontSize(10.5f));
            page.Header().Element(h => Header(h, op, "RÈGLES DU JEU", "#37474F"));
            page.Footer().Element(Footer);
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(5);
                col.Item().Text(rule.Title).FontSize(18).Bold();
                foreach (var line in rule.Text.Replace("\r", "").Split('\n'))
                {
                    if (line.StartsWith("# "))
                        col.Item().PaddingTop(8).Text(line[2..]).FontSize(13).Bold();
                    else if (line.StartsWith("- ") || line.StartsWith("* "))
                        col.Item().PaddingLeft(12).Text("•  " + line[2..]);
                    else if (line.Trim().Length > 0)
                        col.Item().Text(line);
                }
            });
        });
    });

    // ----- Éléments communs -----

    private static void Header(IContainer container, Operation op, string title, string color) =>
        container.BorderBottom(2).BorderColor(color).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Text(title).FontSize(11).Bold().FontColor(color).LetterSpacing(0.08f);
            row.RelativeItem().AlignRight().Text(op.Name).FontSize(11).SemiBold();
        });

    private static void Footer(IContainer container) =>
        container.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
            t.Span(L.F("genere_le_x_avec_airsoft_planner_page", DateTime.Now.ToString("d MMMM yyyy HH:mm", French)));
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });

    private static void Box(IContainer container, string title, Action<ColumnDescriptor> content) =>
        container.Background(Colors.Grey.Lighten4).Padding(8).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text(title.ToUpperInvariant()).FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
            content(col);
        });

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
