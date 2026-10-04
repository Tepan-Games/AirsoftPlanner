using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Timer = System.Threading.Timer;
using AirsoftPlanner.Core.Localization;

namespace AirsoftPlanner.Mobile;

/// <summary>
/// Service de premier plan (notification permanente) : mesure la position et l'envoie au PC de l'OP à
/// intervalle régulier, même écran éteint. Les positions mesurées pendant une coupure du Wi-Fi sont gardées
/// et envoyées au retour du réseau.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeLocation)]
public class TrackingService : Service, ILocationListener
{
    public const string ActionStop = "com.tepangames.airsoftplanner.STOP";
    private const string ChannelId = "suivi";
    private const string NewsChannelId = "orga";
    private const int NotificationId = 1;
    private const int MaxPending = 500;

    private readonly List<TrackPoint> _pending = [];
    private LocationManager? _locations;
    private Timer? _timer;
    private int _interval;
    private bool _sending;

    public static void Start(Context context)
    {
        var intent = new Intent(context, typeof(TrackingService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    public static void Stop(Context context) =>
        context.StartService(new Intent(context, typeof(TrackingService)).SetAction(ActionStop));

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Prefs.ApplyLanguage();
        if (intent?.Action == ActionStop || !Prefs.IsEnrolled)
        {
            Prefs.IsTracking = false;
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CreateChannel();
        var notification = BuildNotification(L.T("demarrage_du_suivi"));
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeLocation);
        else
            StartForeground(NotificationId, notification);

        Prefs.IsTracking = true;
        StartLocationUpdates(Prefs.IntervalSeconds);
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _timer?.Dispose();
        _locations?.RemoveUpdates(this);
        Prefs.IsTracking = false;
        base.OnDestroy();
    }

    // ----- Positions -----

    public void OnLocationChanged(Location location)
    {
        lock (_pending)
        {
            _pending.Add(new TrackPoint(location.Latitude, location.Longitude,
                location.HasAccuracy ? location.Accuracy : null, DateTimeOffset.FromUnixTimeMilliseconds(location.Time)));
            if (_pending.Count > MaxPending)
                _pending.RemoveAt(0);
        }

        Prefs.LastLatitude = location.Latitude;
        Prefs.LastLongitude = location.Longitude;
    }

    public void OnProviderDisabled(string provider)
    {
    }

    public void OnProviderEnabled(string provider)
    {
    }

    public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras)
    {
    }

    private void StartLocationUpdates(int intervalSeconds)
    {
        _interval = Math.Max(5, intervalSeconds);
        _locations ??= (LocationManager?)GetSystemService(LocationService);
        _locations?.RemoveUpdates(this);
        try
        {
            // GPS en priorité ; le réseau (Wi-Fi) complète quand le GPS ne capte pas (bâtiment, sous-bois dense).
            var period = _interval * 1000L / 2;
            if (_locations?.IsProviderEnabled(LocationManager.GpsProvider) == true)
                _locations.RequestLocationUpdates(LocationManager.GpsProvider, period, 0, this, Looper.MainLooper);
            if (_locations?.IsProviderEnabled(LocationManager.NetworkProvider) == true)
                _locations.RequestLocationUpdates(LocationManager.NetworkProvider, period, 0, this, Looper.MainLooper);
        }
        catch (Java.Lang.SecurityException)
        {
            UpdateStatus(L.T("autorisation_de_localisation_refusee_ouvrez_l_ap"));
        }

        _timer?.Dispose();
        _timer = new Timer(_ => _ = SendAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(_interval));
    }

    // ----- Envoi -----

    private async Task SendAsync()
    {
        if (_sending)
            return;
        _sending = true;
        List<TrackPoint> batch;
        lock (_pending)
            batch = [.. _pending];

        try
        {
            var response = await ServerApi.TrackAsync(Prefs.ServerUrl, Prefs.Token, batch);
            lock (_pending)
                _pending.RemoveAll(batch.Contains);

            var previous = Prefs.LastResponse;
            Prefs.LastResponse = response;
            Prefs.MessageArchive = MessageHistory.Merge(Prefs.MessageArchive, response.Messages ?? []);
            await DownloadPhotosAsync();
            NotifyNews(previous, response);
            await DownloadMapIfNeededAsync(response);
            if (response.IntervalSeconds != _interval)
            {
                Prefs.IntervalSeconds = response.IntervalSeconds;
                new Handler(Looper.MainLooper!).Post(() => StartLocationUpdates(response.IntervalSeconds));
            }

            UpdateStatus(L.F("dernier_envoi_a_x_x", DateTime.Now, response.Team) +
                         (response.Mission is { } m ? $" · {(m.IsCurrent ? L.T("en_cours") : "prochaine")} : {m.Name}" : ""));
        }
        catch (ServerApi.RevokedException ex)
        {
            Prefs.Unenroll();
            UpdateStatus($"⛔ {ex.Message}"); // message conservé pour l'écran d'enrôlement
            StopSelf();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // L'IP ou le port du PC a pu changer : on le cherche sur le Wi-Fi (seulement le PC de notre OP).
            if (Prefs.OperationId is { } operation && await ServerApi.DiscoverAsync(operation, Prefs.ServerUrl) is { } found
                && found != ServerApi.Normalize(Prefs.ServerUrl))
            {
                Prefs.ServerUrl = found;
                UpdateStatus(L.F("pc_de_l_op_retrouve_sur_le_wi_fi_x", found));
                _ = Task.Delay(500).ContinueWith(_ => SendAsync());
                return;
            }

            int count;
            lock (_pending)
                count = _pending.Count;
            UpdateStatus(L.F("pc_de_l_op_injoignable_x_x_position_s_en_attente", DateTime.Now, count));
        }
        finally
        {
            _sending = false;
        }
    }

    /// <summary>Fond de carte téléchargé une fois (mode carte autorisé par l'orga), conservé sur le téléphone.</summary>
    private async Task DownloadMapIfNeededAsync(TrackResponse response)
    {
        if (response.ShareMode != AllyShareMode.Map || response.Map is null)
            return;

        var file = MapFile(this);
        var stamp = $"{response.Map.Name}|{response.Map.North}|{response.Map.West}";
        var stampFile = file + ".id";
        if (File.Exists(file) && File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp)
            return;

        if (await ServerApi.MapImageAsync(Prefs.ServerUrl, Prefs.Token) is { } image)
        {
            await File.WriteAllBytesAsync(file, image);
            await File.WriteAllTextAsync(stampFile, stamp);
        }
    }

    /// <summary>Photos jointes aux messages : téléchargées une fois, conservées pour l'historique.</summary>
    private async Task DownloadPhotosAsync()
    {
        foreach (var message in Prefs.MessageArchive.Where(m => m.HasPhoto && !File.Exists(PhotoFile(this, m.Id))).TakeLast(5))
        {
            if (await ServerApi.MessagePhotoAsync(Prefs.ServerUrl, Prefs.Token, message.Id) is not { } photo)
                continue;
            var file = PhotoFile(this, message.Id);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file + ".tmp", photo);
            File.Move(file + ".tmp", file, overwrite: true);
        }
    }

    public static string PhotoFile(Context context, Guid id) => System.IO.Path.Combine(context.FilesDir!.AbsolutePath, "photos", $"{id}.jpg");

    public static string MapFile(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "carte.jpg");

    // ----- Notification -----

    /// <summary>
    /// Signale toute nouvelle information de l'orga : messages (dont mission diffusée ou terminée),
    /// plan radio ou numéro d'urgence modifiés, niveau de difficulté changé.
    /// </summary>
    private void NotifyNews(TrackResponse? previous, TrackResponse response)
    {
        var notified = Prefs.NotifiedMessages.ToHashSet();
        var fresh = (response.Messages ?? []).Where(m => !notified.Contains(m.Id.ToString())).OrderBy(m => m.SentAt).ToList();
        foreach (var message in fresh.Where(m => m.SentAt >= Prefs.EnrolledAt))
        {
            var title = message.Kind switch
            {
                AirsoftPlanner.Core.Domain.MessageKind.MissionAssigned => L.T("nouvelle_mission"),
                AirsoftPlanner.Core.Domain.MessageKind.MissionEnded => L.T("mission_terminee_2"),
                _ when message.Sender == AirsoftPlanner.Core.Domain.MessageSender.Hq => L.F("message_du_qg_x", message.Audience),
                _ => L.F("message_de_l_orga_x", message.Audience),
            };
            Notify(title, message.Text, message.Id.GetHashCode());
        }

        if (fresh.Count > 0)
            Prefs.NotifiedMessages = [.. Prefs.NotifiedMessages, .. fresh.Select(m => m.Id.ToString())];

        if (previous is null)
            return;
        if (Describe(previous.Comms) != Describe(response.Comms))
            Notify(L.T("plan_radio_mis_a_jour"), response.Comms?.EmergencyPhone is { Length: > 0 } phone && phone != previous.Comms?.EmergencyPhone
                ? L.F("nouveau_numero_d_urgence_de_l_orga_x", phone)
                : L.T("les_frequences_de_la_faction_ou_de_l_orga_ont_ch"), 2);
        if (string.Join("|", (previous.Points ?? []).Select(p => $"{p.Name}{p.Coordinates}"))
            != string.Join("|", (response.Points ?? []).Select(p => $"{p.Name}{p.Coordinates}")))
            Notify(L.T("points_d_interet_mis_a_jour"), string.Join(", ", (response.Points ?? []).Select(p => p.Name).Take(6)), 4);
        if (previous.Difficulty != response.Difficulty)
            Notify(L.F("niveau_de_difficulte_x", AirsoftPlanner.Core.Domain.HqDifficultyRules.Label(response.Difficulty)),
                AirsoftPlanner.Core.Domain.HqDifficultyRules.Explanation(response.Difficulty), 3);
    }

    private static string Describe(Comms? comms) => comms is null
        ? ""
        : $"{comms.FactionFrequency}|{comms.OrgaFrequency}|{comms.EmergencyPhone}|{string.Join(";", comms.Teams.Select(t => $"{t.Team}={t.Frequency}"))}";

    private void Notify(string title, string text, int id)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, NewsChannelId) : new Notification.Builder(this);
        var notification = builder
            .SetContentTitle(title)!
            .SetContentText(text)!
            .SetStyle(new Notification.BigTextStyle().BigText(text))!
            .SetSmallIcon(Resource.Drawable.ic_notification)!
            .SetAutoCancel(true)!
            .SetContentIntent(open)!
            .Build()!;
        // Identifiant 1 réservé à la notification permanente du suivi.
        ((NotificationManager?)GetSystemService(NotificationService))?.Notify(id == NotificationId ? id + 1 : id, notification);
    }

    private void UpdateStatus(string text)
    {
        Prefs.Status = text;
        ((NotificationManager?)GetSystemService(NotificationService))?.Notify(NotificationId, BuildNotification(text));
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var channel = new NotificationChannel(ChannelId, L.T("suivi_de_l_equipe"), NotificationImportance.Low)
        {
            Description = L.T("envoi_de_la_position_de_l_equipe_au_pc_de_l_op"),
        };
        var news = new NotificationChannel(NewsChannelId, L.T("informations_de_l_orga"), NotificationImportance.High)
        {
            Description = L.T("messages_de_l_orga_missions_diffusees_plan_radio"),
        };
        news.EnableVibration(true);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
        manager?.CreateNotificationChannel(news);
    }

    private Notification BuildNotification(string text)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, ChannelId) : new Notification.Builder(this);
        return builder
            .SetContentTitle(L.F("airsoft_planner_x_2", Prefs.Team))!
            .SetContentText(text)!
            .SetSmallIcon(Resource.Drawable.ic_notification)!
            .SetOngoing(true)!
            .SetContentIntent(open)!
            .Build()!;
    }
}
