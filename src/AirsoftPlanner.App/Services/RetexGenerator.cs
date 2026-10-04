using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Retex;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AirsoftPlanner.App.Services;

/// <summary>Contexte commun des RETEX : OP, fond de carte, couleurs des factions.</summary>
public record RetexContext(Operation Operation, MapLayer? Map, IReadOnlyDictionary<Guid, string> FactionColors, IReadOnlyDictionary<Guid, string> FactionNames);

/// <summary>RETEX en PDF : bilan global de l'OP et fiche par équipe (missions, trajet, messages reçus).</summary>
public static class RetexGenerator
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    static RetexGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    public static void WriteGlobal(OperationRetex retex, RetexContext context, string path) => Document.Create(container =>
    {
        var op = context.Operation;
        container.Page(page =>
        {
            Setup(page, op, "RETEX — BILAN DE L'OP");
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(op.Name).FontSize(20).Bold();
                col.Item().Text(t =>
                {
                    t.Span($"Du {op.StartsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French)} au {op.EndsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French)}");
                    if (op.OrganizerName.Length > 0)
                        t.Span($" · organisé par {op.OrganizerName}");
                });

                var totals = (Planned: retex.Teams.Sum(t => t.MissionsPlanned), Published: retex.Teams.Sum(t => t.MissionsPublished),
                    Completed: retex.Teams.Sum(t => t.MissionsCompleted), Km: retex.Teams.Sum(t => t.DistanceKm), Out: retex.Teams.Sum(t => t.PlayersOut));
                col.Item().Background(Colors.Grey.Lighten4).Padding(8).Row(row =>
                {
                    Figure(row, "Équipes", retex.Teams.Count.ToString(French));
                    Figure(row, "Missions diffusées", $"{totals.Published}/{totals.Planned}");
                    Figure(row, "Missions terminées", totals.Completed.ToString(French));
                    Figure(row, "Distance parcourue", $"{totals.Km:0.0} km");
                    Figure(row, "Sorties de jeu", totals.Out.ToString(French));
                });

                col.Item().Text("Bilan par équipe").FontSize(14).Bold();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Équipe", "Diffusées", "Terminées", "Retard moyen", "Distance", "Hors jeu", "Messages" })
                            h.Cell().BorderBottom(1).PaddingBottom(2).Text(title).SemiBold();
                    });
                    foreach (var team in retex.Teams)
                    {
                        table.Cell().PaddingVertical(2).Text(t =>
                        {
                            t.Span(team.Team.Name).SemiBold();
                            if (team.Team.FactionId is { } f && context.FactionNames.TryGetValue(f, out var faction))
                                t.Span($"  {faction}").FontColor(Colors.Grey.Darken1);
                        });
                        table.Cell().PaddingVertical(2).Text($"{team.MissionsPublished}/{team.MissionsPlanned}");
                        table.Cell().PaddingVertical(2).Text(team.MissionsCompleted.ToString(French));
                        table.Cell().PaddingVertical(2).Text(AverageDelay(team));
                        table.Cell().PaddingVertical(2).Text($"{team.DistanceKm:0.0} km");
                        table.Cell().PaddingVertical(2).Text(team.PlayersOut.ToString(French));
                        table.Cell().PaddingVertical(2).Text(team.Messages.Count.ToString(French));
                    }
                });

                col.Item().PaddingTop(6).Text("Chronologie").FontSize(14).Bold();
                if (retex.Timeline.Count == 0)
                    col.Item().Text("Aucun événement enregistré.").Italic();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.ConstantColumn(70); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(6); });
                    foreach (var e in retex.Timeline)
                    {
                        table.Cell().PaddingVertical(1).Text(e.At.LocalDateTime.ToString("ddd HH:mm", French)).FontColor(Colors.Grey.Darken2);
                        table.Cell().PaddingVertical(1).Text(e.Team).SemiBold();
                        table.Cell().PaddingVertical(1).Text(e.Category).FontColor(Colors.Grey.Darken2);
                        table.Cell().PaddingVertical(1).Text(e.Text);
                    }
                });
            });
        });

        if (context.Map is { } map)
            MapPage(container, op, "RETEX — TRAJETS DES ÉQUIPES", map,
                retex.Teams.Select(t => (Color(context, t.Team), (IReadOnlyList<GeoPoint>)t.Trail)), "Trajets de toutes les équipes, aux couleurs de leur faction.");
    }).GeneratePdf(path);

    public static void WriteTeam(TeamRetex team, RetexContext context, string path) => Document.Create(container =>
    {
        var op = context.Operation;
        container.Page(page =>
        {
            Setup(page, op, $"RETEX — {team.Team.Name.ToUpperInvariant()}");
            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(t =>
                {
                    t.Span(team.Team.Name).FontSize(20).Bold();
                    if (team.Team.FactionId is { } f && context.FactionNames.TryGetValue(f, out var faction))
                        t.Span($"   {faction}").FontSize(14).FontColor(Color(context, team.Team)).SemiBold();
                });
                col.Item().Text(op.Name).FontColor(Colors.Grey.Darken2);

                col.Item().Background(Colors.Grey.Lighten4).Padding(8).Row(row =>
                {
                    Figure(row, "Missions diffusées", $"{team.MissionsPublished}/{team.MissionsPlanned}");
                    Figure(row, "Terminées", team.MissionsCompleted.ToString(French));
                    Figure(row, "Distance", $"{team.DistanceKm:0.0} km");
                    Figure(row, "Positions reçues", team.PositionCount.ToString(French));
                    Figure(row, "Sorties de jeu", team.PlayersOut.ToString(French));
                });

                col.Item().Text("Missions").FontSize(14).Bold();
                if (team.Missions.Count == 0)
                    col.Item().Text("Aucune mission au programme.").Italic();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(3); c.RelativeColumn(3); c.RelativeColumn(3); c.RelativeColumn(2); });
                    table.Header(h =>
                    {
                        foreach (var title in new[] { "Mission", "Prévue", "Diffusée", "Terminée", "Durée" })
                            h.Cell().BorderBottom(1).PaddingBottom(2).Text(title).SemiBold();
                    });
                    foreach (var m in team.Missions)
                    {
                        table.Cell().PaddingVertical(2).Text(m.Mission.Name);
                        table.Cell().PaddingVertical(2).Text($"{m.PlannedStart.LocalDateTime:HH:mm}–{m.PlannedEnd.LocalDateTime:HH:mm}");
                        table.Cell().PaddingVertical(2).Text(m.PublishedAt is { } p
                            ? $"{p.LocalDateTime:HH:mm} ({Delay(m.DiffusionDelayMinutes!.Value)})"
                            : "non diffusée").FontColor(m.PublishedAt is null ? Colors.Grey.Darken1 : Colors.Black);
                        table.Cell().PaddingVertical(2).Text(m.EndedAt is { } e ? e.LocalDateTime.ToString("HH:mm") : m.Completed ? "oui" : "—");
                        table.Cell().PaddingVertical(2).Text(m.ActualMinutes is { } d ? $"{d:0} min" : "—");
                    }
                });

                col.Item().PaddingTop(6).Text($"Messages transmis à l'équipe ({team.Messages.Count})").FontSize(14).Bold();
                if (team.Messages.Count == 0)
                    col.Item().Text("Aucun message transmis.").Italic();
                foreach (var message in team.Messages)
                    col.Item().BorderLeft(3).BorderColor(message.Sender == MessageSender.Hq ? Colors.Blue.Darken1 : Colors.Orange.Darken2).PaddingLeft(8).Column(m =>
                    {
                        m.Item().Text(t =>
                        {
                            t.Span(message.SentAt.LocalDateTime.ToString("ddd HH:mm", French)).SemiBold();
                            t.Span($"  {(message.Sender == MessageSender.Hq ? "QG" : "Orga")}").FontColor(Colors.Grey.Darken2);
                        });
                        if (message.Text.Length > 0)
                            m.Item().Text(message.Text);
                        if (message.Photo is { Length: > 0 } photo)
                            m.Item().PaddingTop(3).MaxWidth(8, Unit.Centimetre).Image(photo).FitWidth();
                    });

                var other = team.Events.Where(e => e.Category is "Objet" or "Effectif").ToList();
                if (other.Count > 0)
                {
                    col.Item().PaddingTop(6).Text("Objets d'objectif et effectif").FontSize(14).Bold();
                    foreach (var e in other)
                        col.Item().Text(t =>
                        {
                            t.Span(e.At.LocalDateTime.ToString("ddd HH:mm", French)).SemiBold();
                            t.Span($"  {e.Category} — {e.Text}");
                        });
                }
            });
        });

        if (context.Map is { } map && team.Trail.Count >= 2)
            MapPage(container, op, $"RETEX — TRAJET : {team.Team.Name.ToUpperInvariant()}", map,
                [(Color(context, team.Team), team.Trail)], $"Trajet de l'équipe {team.Team.Name} pendant l'OP ({team.DistanceKm:0.0} km).");
    }).GeneratePdf(path);

    // ----- Éléments communs -----

    private static void Setup(PageDescriptor page, Operation op, string title)
    {
        page.Size(PageSizes.A4);
        page.Margin(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(t => t.FontSize(10));
        page.Header().BorderBottom(2).BorderColor("#37474F").PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Text(title).FontSize(11).Bold().FontColor("#37474F").LetterSpacing(0.08f);
            row.RelativeItem().AlignRight().Text(op.Name).FontSize(11).SemiBold();
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
            t.Span($"RETEX généré le {DateTime.Now.ToString("d MMMM yyyy HH:mm", French)} avec Airsoft Planner — page ");
            t.CurrentPageNumber();
            t.Span(" / ");
            t.TotalPages();
        });
    }

    private static void MapPage(IDocumentContainer container, Operation op, string title, MapLayer map,
        IEnumerable<(string Color, IReadOnlyList<GeoPoint> Points)> trails, string caption)
    {
        var image = MapSnapshot.Render(map, [], trails: trails);
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1, Unit.Centimetre);
            page.Header().Text(title).FontSize(11).Bold().FontColor("#37474F");
            page.Footer().Text($"{caption} © {map.Attribution}").FontSize(8).FontColor(Colors.Grey.Darken1);
            page.Content().PaddingVertical(6).AlignCenter().Image(image).FitArea();
        });
    }

    private static void Figure(RowDescriptor row, string label, string value) => row.RelativeItem().Column(c =>
    {
        c.Item().Text(value).FontSize(16).Bold();
        c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
    });

    private static string Color(RetexContext context, Team team) =>
        team.FactionId is { } f && context.FactionColors.TryGetValue(f, out var color) ? color : "#607D8B";

    public static string AverageDelay(TeamRetex team)
    {
        var delays = team.Missions.Select(m => m.DiffusionDelayMinutes).OfType<double>().ToList();
        return delays.Count == 0 ? "—" : Delay(delays.Average());
    }

    private static string Delay(double minutes) => Math.Abs(minutes) < 1 ? "à l'heure"
        : minutes > 0 ? $"+{minutes:0} min" : $"{minutes:0} min";
}
