using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Timer = System.Threading.Timer;

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
        if (intent?.Action == ActionStop || !Prefs.IsEnrolled)
        {
            Prefs.IsTracking = false;
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        CreateChannel();
        var notification = BuildNotification("Démarrage du suivi…");
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
            UpdateStatus("⚠ Autorisation de localisation refusée : ouvrez l'application pour l'accorder.");
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

            Prefs.LastResponse = response;
            await DownloadMapIfNeededAsync(response);
            if (response.IntervalSeconds != _interval)
            {
                Prefs.IntervalSeconds = response.IntervalSeconds;
                new Handler(Looper.MainLooper!).Post(() => StartLocationUpdates(response.IntervalSeconds));
            }

            UpdateStatus($"Dernier envoi à {DateTime.Now:HH:mm:ss} · {response.Team}" +
                         (response.Mission is { } m ? $" · {(m.IsCurrent ? "en cours" : "prochaine")} : {m.Name}" : ""));
        }
        catch (ServerApi.RevokedException ex)
        {
            UpdateStatus($"⛔ {ex.Message}");
            Prefs.Unenroll();
            StopSelf();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            int count;
            lock (_pending)
                count = _pending.Count;
            UpdateStatus($"⚠ PC de l'OP injoignable ({DateTime.Now:HH:mm}) · {count} position(s) en attente");
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

    public static string MapFile(Context context) => Path.Combine(context.FilesDir!.AbsolutePath, "carte.jpg");

    // ----- Notification -----

    private void UpdateStatus(string text)
    {
        Prefs.Status = text;
        ((NotificationManager?)GetSystemService(NotificationService))?.Notify(NotificationId, BuildNotification(text));
    }

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var channel = new NotificationChannel(ChannelId, "Suivi de l'équipe", NotificationImportance.Low)
        {
            Description = "Envoi de la position de l'équipe au PC de l'OP",
        };
        ((NotificationManager?)GetSystemService(NotificationService))?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string text)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, ChannelId) : new Notification.Builder(this);
        return builder
            .SetContentTitle($"Airsoft Planner — {Prefs.Team}")!
            .SetContentText(text)!
            .SetSmallIcon(Resource.Mipmap.appicon)!
            .SetOngoing(true)!
            .SetContentIntent(open)!
            .Build()!;
    }
}
