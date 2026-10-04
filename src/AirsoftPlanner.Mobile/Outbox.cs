using System.Text.Json;
using System.Text.Json.Serialization;
using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Graphics;

namespace AirsoftPlanner.Mobile;

/// <summary>Message du téléphone vers l'orga, conservé sur le téléphone (historique, renvoi après une coupure).</summary>
internal record OutgoingReport(Guid Id, string Text, bool HasPhoto, DateTimeOffset SentAt, double? Latitude, double? Longitude, bool Delivered,
    AirsoftPlanner.Core.Domain.MessageSender Recipient = AirsoftPlanner.Core.Domain.MessageSender.Orga);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<OutgoingReport>))]
internal partial class OutboxJson : JsonSerializerContext;

/// <summary>
/// Messages envoyés au QG ou à l'orga : enregistrés d'abord sur le téléphone (photo dans un fichier), puis envoyés au PC de l'OP ;
/// ceux qui n'ont pas pu partir (Wi-Fi perdu) sont renvoyés après chaque échange réussi.
/// </summary>
internal static class Outbox
{
    private const string Key = "Outbox";
    private static readonly object Gate = new();

    private static ISharedPreferences Store => Application.Context.GetSharedPreferences("airsoftplanner", FileCreationMode.Private)!;

    public static IReadOnlyList<OutgoingReport> All
    {
        get
        {
            lock (Gate)
                return Store.GetString(Key, "") is { Length: > 0 } json ? JsonSerializer.Deserialize(json, OutboxJson.Default.ListOutgoingReport) ?? [] : [];
        }
    }

    public static string PhotoFile(Guid id) => System.IO.Path.Combine(Application.Context.FilesDir!.AbsolutePath, "photos", "envoyees", $"{id}.jpg");

    /// <summary>Enregistre un message à envoyer (la photo, déjà réduite, est conservée dans un fichier).</summary>
    public static OutgoingReport Add(string text, byte[]? photo, AirsoftPlanner.Core.Domain.MessageSender recipient)
    {
        var report = new OutgoingReport(Guid.NewGuid(), text.Trim(), photo is { Length: > 0 }, DateTimeOffset.Now,
            Prefs.LastLatitude, Prefs.LastLongitude, false, recipient);
        if (photo is { Length: > 0 })
        {
            var file = PhotoFile(report.Id);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, photo);
        }
        Update(list => list.Add(report));
        return report;
    }

    /// <summary>Envoie les messages en attente ; vrai si tous sont partis.</summary>
    public static async Task<bool> SendPendingAsync()
    {
        foreach (var report in All.Where(r => !r.Delivered))
        {
            var photo = report.HasPhoto && File.Exists(PhotoFile(report.Id)) ? await File.ReadAllBytesAsync(PhotoFile(report.Id)) : null;
            try
            {
                await ServerApi.ReportAsync(Prefs.ServerUrl,
                    new ReportRequest(Prefs.Token, report.Id, report.Text, photo, report.SentAt, report.Latitude, report.Longitude, report.Recipient));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ServerApi.RevokedException)
            {
                // PC injoignable (renvoyé plus tard) ou téléphone révoqué (l'écran d'enrôlement s'affichera).
                return false;
            }
            Update(list => list[list.FindIndex(r => r.Id == report.Id)] = report with { Delivered = true });
        }
        return true;
    }

    public static void Clear()
    {
        lock (Gate)
            Store.Edit()!.Remove(Key)!.Apply();
    }

    private static void Update(Action<List<OutgoingReport>> change)
    {
        lock (Gate)
        {
            var list = Store.GetString(Key, "") is { Length: > 0 } json ? JsonSerializer.Deserialize(json, OutboxJson.Default.ListOutgoingReport) ?? [] : [];
            change(list);
            Store.Edit()!.PutString(Key, JsonSerializer.Serialize(list.TakeLast(200).ToList(), OutboxJson.Default.ListOutgoingReport))!.Apply();
        }
    }

    /// <summary>Photo réduite (1280 px au plus, JPEG) : envoi rapide sur le Wi-Fi du terrain.</summary>
    public static byte[]? Shrink(Context context, Android.Net.Uri uri)
    {
        var resolver = context.ContentResolver!;
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        using (var probe = resolver.OpenInputStream(uri))
            BitmapFactory.DecodeStream(probe, null, bounds);
        if (bounds.OutWidth <= 0)
            return null;
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= 1280)
            sample *= 2;
        using var input = resolver.OpenInputStream(uri);
        using var decoded = BitmapFactory.DecodeStream(input, null, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null)
            return null;
        var scale = Math.Min(1.0, 1280.0 / Math.Max(decoded.Width, decoded.Height));
        using var resized = scale < 1 ? Bitmap.CreateScaledBitmap(decoded, (int)(decoded.Width * scale), (int)(decoded.Height * scale), true)! : decoded;
        using var stream = new MemoryStream();
        resized.Compress(Bitmap.CompressFormat.Jpeg!, 80, stream);
        return stream.ToArray();
    }
}
