using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirsoftPlanner.App.Services.Pdf;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Planning;
using AirsoftPlanner.Core.Retex;
using AirsoftPlanner.Core.Localization;
using MigraDoc.DocumentObjectModel;

namespace AirsoftPlanner.App.Services;

/// <summary>Contexte commun des RETEX : OP, fond de carte, couleurs des factions.</summary>
public record RetexContext(Operation Operation, MapLayer? Map, IReadOnlyDictionary<Guid, string> FactionColors, IReadOnlyDictionary<Guid, string> FactionNames);

/// <summary>RETEX en PDF : bilan global de l'OP et fiche par équipe (missions, trajet, messages reçus).</summary>
public static class RetexGenerator
{
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;

    private static readonly TextStyle Head = new(SemiBold: true);
    private static readonly TextStyle Muted = new(Color: PdfColors.GreyDarken2);

    public static void WriteGlobal(OperationRetex retex, RetexContext context, string path)
    {
        var op = context.Operation;
        var document = new PdfDoc(10);
        var col = document.Section(header: ("RETEX — BILAN DE L'OP", op.Name, "#37474F", 2), footer: FooterText());
        col.Spacing = 10;
        col.Text(op.Name, new TextStyle(20, Bold: true));
        col.Line((L.F("du_x_au_x", op.StartsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French), op.EndsAt.LocalDateTime.ToString("dddd d MMMM yyyy HH:mm", French)), default),
            (op.OrganizerName.Length > 0 ? L.F("organise_par_x", op.OrganizerName) : "", default));

        var totals = (Planned: retex.Teams.Sum(t => t.MissionsPlanned), Published: retex.Teams.Sum(t => t.MissionsPublished),
            Completed: retex.Teams.Sum(t => t.MissionsCompleted), Km: retex.Teams.Sum(t => t.DistanceKm), Out: retex.Teams.Sum(t => t.PlayersOut));
        Figures(col,
            (L.T("equipes"), retex.Teams.Count.ToString(French)),
            (L.T("missions_diffusees"), $"{totals.Published}/{totals.Planned}"),
            (L.T("missions_terminees"), totals.Completed.ToString(French)),
            (L.T("distance_parcourue"), $"{totals.Km:0.0} km"),
            (L.T("sorties_de_jeu"), totals.Out.ToString(French)));

        // Score et résultats : chaque mission compte une fois (elle peut être commune à plusieurs équipes).
        var missions = retex.Teams.SelectMany(t => t.Missions.Select(m => m.Mission)).Distinct().OrderBy(m => m.StartMinutes).ToList();
        if (missions.Any(m => m.Result != MissionResult.NotEvaluated))
        {
            col.Text(L.T("score_par_faction"), new TextStyle(14, Bold: true));
            var factions = context.FactionNames.Select(f => (f.Key, f.Value, context.FactionColors.GetValueOrDefault(f.Key, "#607D8B")));
            col.Table([4, 2, 2, 2, 2],
                [L.T("faction"), L.T("points"), L.T("resultat_reussie"), L.T("resultat_partielle"), L.T("resultat_echouee")],
                Scoreboard.ByFaction(missions, factions, retex.Teams.Select(t => t.Team)).Select(line => (IReadOnlyList<(string, TextStyle)>)
                [
                    (line.Name, new TextStyle(SemiBold: true, Color: line.Color)),
                    (line.Points.ToString(French), new TextStyle(Bold: true)),
                    (line.Success.ToString(French), default), (line.Partial.ToString(French), default), (line.Failure.ToString(French), default),
                ]));
        }

        col.Text(L.T("resultats_des_missions"), new TextStyle(14, Bold: true));
        var planned = retex.Teams.SelectMany(t => t.Missions).GroupBy(m => m.Mission).ToDictionary(g => g.Key, g => g.First().PlannedStart);
        col.Table([2, 4, 4, 3, 5],
            [L.T("prevue"), L.T("mission_2"), L.T("equipes"), L.T("resultat"), L.T("commentaire")],
            missions.Select(mission => (IReadOnlyList<(string, TextStyle)>)
            [
                (planned[mission].LocalDateTime.ToString("ddd HH:mm", French), Muted),
                (mission.Name, Head),
                (string.Join(", ", retex.Teams.Where(t => mission.TeamIds.Contains(t.Team.Id)).Select(t => t.Team.Name)), default),
                (ResultText(mission), new TextStyle(Color: ResultColor(mission.Result))),
                (mission.ResultNotes, default),
            ]));

        col.Text(L.T("bilan_par_equipe"), new TextStyle(14, Bold: true));
        col.Table([3, 2, 2, 2, 2, 2, 2, 2],
            [L.T("equipe"), L.T("diffusees"), L.T("terminees"), L.T("points"), L.T("retard_moyen"), L.T("distance"), L.T("hors_jeu"), L.T("messages")],
            retex.Teams.Select(team => (IReadOnlyList<(string, TextStyle)>)
            [
                (team.Team.Name + (team.Team.FactionId is { } f && context.FactionNames.TryGetValue(f, out var faction) ? $"  {faction}" : ""), Head),
                ($"{team.MissionsPublished}/{team.MissionsPlanned}", default),
                (team.MissionsCompleted.ToString(French), default),
                (team.Points.ToString(French), default),
                (AverageDelay(team), default),
                ($"{team.DistanceKm:0.0} km", default),
                (team.PlayersOut.ToString(French), default),
                (team.Messages.Count.ToString(French), default),
            ]));

        col.Text(L.T("chronologie"), new TextStyle(14, Bold: true), spaceBefore: 6);
        if (retex.Timeline.Count == 0)
            col.Text(L.T("aucun_evenement_enregistre"), new TextStyle(Italic: true));
        else
            col.Table([1.8, 2, 2, 6], null, retex.Timeline.Select(e => (IReadOnlyList<(string, TextStyle)>)
            [
                (e.At.LocalDateTime.ToString("ddd HH:mm", French), Muted), (e.Team, Head), (e.Category, Muted), (e.Text, default),
            ]), rowPadding: 1);

        if (context.Map is { } map)
            MapPage(document, op, "RETEX — TRAJETS DES ÉQUIPES", map,
                retex.Teams.Select(t => (Color(context, t.Team), (IReadOnlyList<GeoPoint>)t.Trail)), L.T("trajets_de_toutes_les_equipes_aux_couleurs_de_le"));
        document.Save(path);
    }

    public static void WriteTeam(TeamRetex team, RetexContext context, string path)
    {
        var op = context.Operation;
        var document = new PdfDoc(10);
        var col = document.Section(header: ($"RETEX — {team.Team.Name.ToUpperInvariant()}", op.Name, "#37474F", 2), footer: FooterText());
        col.Spacing = 10;
        col.Line((team.Team.Name, new TextStyle(20, Bold: true)),
            (team.Team.FactionId is { } f && context.FactionNames.TryGetValue(f, out var faction) ? $"   {faction}" : "",
                new TextStyle(14, SemiBold: true, Color: Color(context, team.Team))));
        col.Text(op.Name, Muted);

        Figures(col,
            (L.T("missions_diffusees"), $"{team.MissionsPublished}/{team.MissionsPlanned}"),
            (L.T("terminees"), team.MissionsCompleted.ToString(French)),
            (L.T("points"), team.Points.ToString(French)),
            (L.T("distance"), $"{team.DistanceKm:0.0} km"),
            (L.T("positions_recues"), team.PositionCount.ToString(French)),
            (L.T("sorties_de_jeu"), team.PlayersOut.ToString(French)));

        col.Text("Missions", new TextStyle(14, Bold: true));
        if (team.Missions.Count == 0)
            col.Text(L.T("aucune_mission_au_programme"), new TextStyle(Italic: true));
        else
        {
            var rows = new List<IReadOnlyList<(string, TextStyle)>>();
            foreach (var m in team.Missions)
            {
                rows.Add(
                [
                    (m.Mission.Name, default),
                    ($"{m.PlannedStart.LocalDateTime:HH:mm}–{m.PlannedEnd.LocalDateTime:HH:mm}", default),
                    (m.PublishedAt is { } p ? $"{p.LocalDateTime:HH:mm} ({Delay(m.DiffusionDelayMinutes!.Value)})" : L.T("non_diffusee"),
                        new TextStyle(Color: m.PublishedAt is null ? PdfColors.GreyDarken1 : PdfColors.Black)),
                    (m.EndedAt is { } e ? e.LocalDateTime.ToString("HH:mm") : m.Completed ? "oui" : "—", default),
                    (m.ActualMinutes is { } d ? $"{d:0} min" : "—", default),
                    (ResultText(m.Mission), new TextStyle(Color: ResultColor(m.Mission.Result))),
                ]);
                // Commentaire du résultat sous la ligne de la mission.
                if (m.Mission.ResultNotes.Length > 0)
                    rows.Add([("    " + m.Mission.ResultNotes, new TextStyle(Italic: true, Color: PdfColors.GreyDarken2))]);
            }
            var table = col.Table([4, 3, 3, 3, 2, 3],
                [L.T("mission_2"), L.T("prevue"), L.T("diffusee"), L.T("terminee"), L.T("duree"), L.T("resultat")], rows);
            for (var i = 1; i < table.Rows.Count; i++)
                if (rows[i - 1].Count == 1)
                    table.Rows[i].Cells[0].MergeRight = 5;
        }

        col.Text(L.F("messages_transmis_a_l_equipe_x", team.Messages.Count), new TextStyle(14, Bold: true), spaceBefore: 6);
        if (team.Messages.Count == 0)
            col.Text(L.T("aucun_message_transmis"), new TextStyle(Italic: true));
        foreach (var message in team.Messages)
            col.Box(null, m =>
            {
                m.Line((message.SentAt.LocalDateTime.ToString("ddd HH:mm", French), Head),
                    ($"  {(message.Sender == MessageSender.Hq ? "QG" : L.T("orga_2"))}", Muted));
                if (message.Text.Length > 0)
                    m.Text(message.Text);
                if (message.Photo is { Length: > 0 } photo)
                    m.Image(photo, widthCm: 8);
            }, background: message.Sender == MessageSender.Hq ? "#E3F2FD" : "#FFF3E0", padding: 6);

        var other = team.Events.Where(e => e.Category == L.T("objet") || e.Category == L.T("effectif")).ToList();
        if (other.Count > 0)
        {
            col.Text(L.T("objets_d_objectif_et_effectif"), new TextStyle(14, Bold: true), spaceBefore: 6);
            foreach (var e in other)
                col.Line((e.At.LocalDateTime.ToString("ddd HH:mm", French), Head), ($"  {e.Category} — {e.Text}", default));
        }

        if (context.Map is { } map && team.Trail.Count >= 2)
            MapPage(document, op, $"RETEX — TRAJET : {team.Team.Name.ToUpperInvariant()}", map,
                [(Color(context, team.Team), team.Trail)], $"Trajet de l'équipe {team.Team.Name} pendant l'OP ({team.DistanceKm:0.0} km).");
        document.Save(path);
    }

    // ----- Éléments communs -----

    private static string FooterText() =>
        L.F("retex_genere_le_x_avec_airsoft_planner_page", DateTime.Now.ToString("d MMMM yyyy HH:mm", French));

    private static void MapPage(PdfDoc document, Operation op, string title, MapLayer map,
        IEnumerable<(string Color, IReadOnlyList<GeoPoint> Points)> trails, string caption)
    {
        var image = MapSnapshot.Render(map, [], trails: trails);
        var page = document.Section(landscape: true, marginCm: 1, header: (title, op.Name, "#37474F", 1), footer: null,
            footerLines: [$"{caption} © {map.Attribution}"]);
        page.Image(image, alignment: ParagraphAlignment.Center);
    }

    /// <summary>Chiffres clés : grande valeur, petit libellé, sur fond gris.</summary>
    private static void Figures(PdfFlow col, params (string Label, string Value)[] figures) =>
        col.Box(null, b => b.Columns(figures.Select(_ => 1.0).ToArray(), 4, figures.Select(f => (Action<PdfFlow>)(c =>
        {
            c.Spacing = 0;
            c.Text(f.Value, new TextStyle(16, Bold: true));
            c.Text(f.Label, new TextStyle(8, Color: PdfColors.GreyDarken2));
        })).ToArray()));

    /// <summary>« ✔ Réussie (+10) », « Non évaluée ».</summary>
    private static string ResultText(Mission mission) => mission.Result == MissionResult.NotEvaluated
        ? MissionResults.Label(mission.Result)
        : $"{MissionResults.Symbol(mission.Result)} {MissionResults.Label(mission.Result)} ({MissionResults.Points(mission):+0;-0;0})";

    private static string ResultColor(MissionResult result) => result switch
    {
        MissionResult.Success => PdfColors.Green,
        MissionResult.Partial => PdfColors.Orange,
        MissionResult.Failure => PdfColors.Red,
        _ => PdfColors.GreyDarken1,
    };

    private static string Color(RetexContext context, Team team) =>
        team.FactionId is { } f && context.FactionColors.TryGetValue(f, out var color) ? color : "#607D8B";

    public static string AverageDelay(TeamRetex team)
    {
        var delays = team.Missions.Select(m => m.DiffusionDelayMinutes).OfType<double>().ToList();
        return delays.Count == 0 ? "—" : Delay(delays.Average());
    }

    private static string Delay(double minutes) => Math.Abs(minutes) < 1 ? L.T("a_l_heure")
        : minutes > 0 ? L.F("x_min", minutes) : $"{minutes:0} min";
}
