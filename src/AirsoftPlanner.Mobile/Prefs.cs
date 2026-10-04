using System.Text.Json;
using AirsoftPlanner.Core.Gps;
using Android.Content;

namespace AirsoftPlanner.Mobile;

/// <summary>Réglages et dernier état, conservés sur le téléphone (partagés entre l'écran et le service d'envoi).</summary>
internal static class Prefs
{
    private static ISharedPreferences Store => Application.Context.GetSharedPreferences("airsoftplanner", FileCreationMode.Private)!;

    public static string ServerUrl { get => Get(nameof(ServerUrl)); set => Set(nameof(ServerUrl), value); }

    public static string Token { get => Get(nameof(Token)); set => Set(nameof(Token), value); }

    public static string Team { get => Get(nameof(Team)); set => Set(nameof(Team), value); }

    public static string Operation { get => Get(nameof(Operation)); set => Set(nameof(Operation), value); }

    public static string Faction { get => Get(nameof(Faction)); set => Set(nameof(Faction), value); }

    /// <summary>Identifiant de l'OP (recherche du PC sur le Wi-Fi) ; null pour un enrôlement antérieur à cette option.</summary>
    public static Guid? OperationId
    {
        get => Guid.TryParse(Get(nameof(OperationId)), out var id) && id != Guid.Empty ? id : null;
        set => Set(nameof(OperationId), value?.ToString() ?? "");
    }

    /// <summary>Date de l'enrôlement : les messages plus anciens sont affichés sans notification.</summary>
    public static DateTimeOffset EnrolledAt
    {
        get => DateTimeOffset.TryParse(Get(nameof(EnrolledAt)), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var at) ? at : DateTimeOffset.MinValue;
        set => Set(nameof(EnrolledAt), value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Messages de l'orga déjà notifiés (identifiants séparés par des virgules, les plus récents).</summary>
    public static IReadOnlyCollection<string> NotifiedMessages
    {
        get => Get(nameof(NotifiedMessages)).Split(',', StringSplitOptions.RemoveEmptyEntries);
        set => Set(nameof(NotifiedMessages), string.Join(",", value.TakeLast(100)));
    }

    public static string DeviceName { get => Get(nameof(DeviceName)); set => Set(nameof(DeviceName), value); }

    public static int IntervalSeconds
    {
        get => Store.GetInt(nameof(IntervalSeconds), 30);
        set => Store.Edit()!.PutInt(nameof(IntervalSeconds), value)!.Apply();
    }

    public static bool IsTracking
    {
        get => Store.GetBoolean(nameof(IsTracking), false);
        set => Store.Edit()!.PutBoolean(nameof(IsTracking), value)!.Apply();
    }

    /// <summary>Dernière réponse du PC de l'OP (mission, alliés, radio), affichée même hors réseau.</summary>
    public static TrackResponse? LastResponse
    {
        get => Get(nameof(LastResponse)) is { Length: > 0 } json ? JsonSerializer.Deserialize(json, ProtocolJson.Default.TrackResponse) : null;
        set => Set(nameof(LastResponse), value is null ? "" : JsonSerializer.Serialize(value, ProtocolJson.Default.TrackResponse));
    }

    /// <summary>Plan radio reçu à l'enrôlement (avant le premier envoi).</summary>
    public static Comms? EnrollComms
    {
        get => Get(nameof(EnrollComms)) is { Length: > 0 } json
            ? JsonSerializer.Deserialize(json, ProtocolJson.Default.EnrollResponse)?.Comms
            : null;
        set => Set(nameof(EnrollComms), value is null ? "" : JsonSerializer.Serialize(
            new EnrollResponse("", "", "", "", "", 0, AllyShareMode.Coordinates, value), ProtocolJson.Default.EnrollResponse));
    }

    public static string Status { get => Get(nameof(Status)); set => Set(nameof(Status), value); }

    public static double? LastLatitude { get => GetDouble(nameof(LastLatitude)); set => SetDouble(nameof(LastLatitude), value); }

    public static double? LastLongitude { get => GetDouble(nameof(LastLongitude)); set => SetDouble(nameof(LastLongitude), value); }

    public static bool IsEnrolled => Token.Length > 0;

    public static void Unenroll()
    {
        var server = ServerUrl;
        var device = DeviceName;
        Store.Edit()!.Clear()!.Apply();
        ServerUrl = server;
        DeviceName = device;
        // Le message de révocation éventuel est réécrit juste après par le service.
    }

    private static string Get(string key) => Store.GetString(key, "") ?? "";

    private static void Set(string key, string value) => Store.Edit()!.PutString(key, value)!.Apply();

    private static double? GetDouble(string key) =>
        Store.Contains(key) ? BitConverter.Int64BitsToDouble(Store.GetLong(key, 0)) : null;

    private static void SetDouble(string key, double? value)
    {
        var edit = Store.Edit()!;
        if (value is { } v)
            edit.PutLong(key, BitConverter.DoubleToInt64Bits(v));
        else
            edit.Remove(key);
        edit.Apply();
    }
}
