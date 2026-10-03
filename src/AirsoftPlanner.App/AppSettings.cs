using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AirsoftPlanner.Core.Geo;
using Avalonia;
using Avalonia.Styling;

namespace AirsoftPlanner.App;

public enum AppTheme
{
    /// <summary>Suit le thème de Windows.</summary>
    System,
    Day,
    Night,
}

/// <summary>Préférences de l'utilisateur sur ce poste (pas dans le fichier d'OP).</summary>
public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirsoftPlanner", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Current { get; } = Load();

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Format d'affichage des coordonnées dans tout le logiciel (les documents imprimés suivent le réglage de l'OP).</summary>
    public CoordinateFormat CoordinateFormat { get; set; } = CoordinateFormat.Utm;

    // ----- Réception GPS (réglages du poste) -----

    public int GpsServerPort { get; set; } = 5055;

    public string MqttHost { get; set; } = "localhost";

    public int MqttPort { get; set; } = 1883;

    public string MqttTopic { get; set; } = "msh/#";

    public string MqttUser { get; set; } = "";

    /// <summary>Mot de passe chiffré pour l'utilisateur Windows (voir <see cref="Services.Secret"/>).</summary>
    public string MqttPasswordProtected { get; set; } = "";

    public string TraccarUrl { get; set; } = "";

    public string TraccarUser { get; set; } = "";

    public string TraccarPasswordProtected { get; set; } = "";

    /// <summary>Second poste : adresse du serveur local du PC qui mène l'OP.</summary>
    public string UpstreamUrl { get; set; } = "";

    /// <summary>Travail partagé : fichier partagé (dossier synchronisé) de chaque OP, par identifiant d'OP.</summary>
    public System.Collections.Generic.Dictionary<string, string> SharedFiles { get; set; } = [];

    /// <summary>Déclenché quand le format d'affichage des coordonnées change.</summary>
    public static event Action? CoordinateFormatChanged;

    public static void SetCoordinateFormat(CoordinateFormat format)
    {
        if (Current.CoordinateFormat == format)
            return;

        Current.CoordinateFormat = format;
        Current.Save();
        CoordinateFormatChanged?.Invoke();
    }

    public static void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = Current.Theme switch
            {
                AppTheme.Day => ThemeVariant.Light,
                AppTheme.Night => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };

        // Thème « Système » : on fige le choix effectif pour que le bouton bascule vers l'autre mode.
        if (Current.Theme == AppTheme.System && Application.Current is { } current)
            Current.Theme = current.ActualThemeVariant == ThemeVariant.Dark ? AppTheme.Night : AppTheme.Day;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférence non enregistrée : sans conséquence, elle sera redemandée au prochain lancement.
        }
    }

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }
}
