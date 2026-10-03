using AirsoftPlanner.Core.Registration;

namespace AirsoftPlanner.Core.Tests;

public class RegistrationTests
{
    [Fact]
    public void Csv_from_a_french_spreadsheet_is_read()
    {
        const string csv = "﻿Nom de l'équipe;Camp souhaité;Responsable;Téléphone portable;Adresse mail;Nombre de joueurs;Commentaire\r\n" +
                           "Les Faucons;OTAN;Julien Martin;06 12 34 56 78;julien@example.com;8;\r\n" +
                           "\"Team \"\"Delta\"\"\";Insurgés;Sophie;07 00 00 00 00;sophie@example.com;5 joueurs;\"Arrivée tardive;\nvers 10 h\"\r\n";

        var rows = RegistrationCsv.Parse(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new RegistrationRow("Les Faucons", "OTAN", "Julien Martin", "06 12 34 56 78", "julien@example.com", 8, ""), rows[0]);
        Assert.Equal("Team \"Delta\"", rows[1].TeamName);
        Assert.Equal(5, rows[1].Players);
        Assert.Equal("Arrivée tardive;\nvers 10 h", rows[1].Notes);
    }

    [Fact]
    public void Comma_separated_english_export_is_read()
    {
        const string csv = "Timestamp,Team,Leader,Email,Phone,Players\n2026-09-01,Bravo Six,John,john@example.com,0600000000,6\n";

        var row = Assert.Single(RegistrationCsv.Parse(csv));

        Assert.Equal(("Bravo Six", "John", 6), (row.TeamName, row.ContactName, row.Players));
    }

    [Fact]
    public void Missing_team_column_is_reported()
    {
        Assert.Throws<FormatException>(() => RegistrationCsv.Parse("Prénom;Mail\nJean;jean@example.com\n"));
    }

    [Fact]
    public void Written_csv_can_be_read_back()
    {
        var row = new RegistrationRow("Les Faucons", "OTAN", "Julien; dit « Faucon »", "06", "j@example.com", 8, "ligne 1\nligne 2");

        var parsed = Assert.Single(RegistrationCsv.Parse(RegistrationCsv.Write([(row, "Confirmée", DateTimeOffset.Now)])));

        Assert.Equal(row, parsed);
    }

    [Fact]
    public void Waiting_list_is_promoted_first_come_first_served_within_capacity()
    {
        var first = Guid.NewGuid();
        var tooBig = Guid.NewGuid();
        var small = Guid.NewGuid();
        var now = DateTimeOffset.Now;

        var promoted = RegistrationRules.Promotable(maxPlayers: 40, currentPlayers: 30,
        [
            (small, 3, now.AddDays(-1)),
            (first, 6, now.AddDays(-3)),
            (tooBig, 8, now.AddDays(-2)),
        ]);

        Assert.Equal([first, small], promoted);
        Assert.Equal(3, RegistrationRules.Promotable(0, 100, [(first, 6, now), (tooBig, 8, now), (small, 3, now)]).Count);
    }

    [Fact]
    public void Only_pre_registered_and_confirmed_teams_play()
    {
        Assert.True(RegistrationRules.IsPlaying(RegistrationStatus.Confirmed));
        Assert.True(RegistrationRules.IsPlaying(RegistrationStatus.PreRegistered));
        Assert.False(RegistrationRules.IsPlaying(RegistrationStatus.WaitingList));
        Assert.False(RegistrationRules.IsPlaying(RegistrationStatus.Cancelled));
    }
}
