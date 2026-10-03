using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
