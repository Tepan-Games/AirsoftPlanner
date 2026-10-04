using System.Globalization;
using AirsoftPlanner.Core.Domain;
using AirsoftPlanner.Core.Geo;
using AirsoftPlanner.Core.Gps;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;

namespace AirsoftPlanner.Mobile;

/// <summary>
/// Écran du chef d'équipe : enrôlement (QR code ou saisie), puis suivi : envoi de la position, mission en cours,
/// plan radio, numéro d'urgence et alliés (coordonnées ou carte, selon ce que l'orga autorise).
/// </summary>
[Activity(Label = "Airsoft Planner", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, Exported = true,
    ScreenOrientation = ScreenOrientation.Portrait)]
// Le QR code d'enrôlement (airsoftplanner://enroll?...) scanné avec l'appareil photo ouvre directement l'application.
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = EnrollmentLink.Scheme, DataHost = "enroll")]
public class MainActivity : Activity
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly Handler _refresh = new(Looper.MainLooper!);
    private LinearLayout _root = null!;

    // Écran de suivi
    private TextView? _status;
    private Button? _toggle;
    private TextView? _mission;
    private TextView? _comms;
    private Button? _emergency;
    private TextView? _allies;
    private TextView? _ownPosition;
    private MapCanvasView? _map;
    private Bitmap? _mapBitmap;
    private long _mapStamp;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Pas de barre de titre : l'écran affiche son propre titre (équipe).
        Window!.RequestFeature(WindowFeatures.NoTitle);
        _root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _root.SetPadding(Dp(16), Dp(16), Dp(16), Dp(24));
        var scroll = new ScrollView(this);
        scroll.AddView(_root);
        // Android 15 dessine l'application sous les barres système : on décale le contenu d'autant.
        scroll.SetOnApplyWindowInsetsListener(new InsetsListener(_root, Dp(16), Dp(24)));
        SetContentView(scroll);
        Show();
        HandleLink(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleLink(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ScheduleRefresh();
    }

    protected override void OnPause()
    {
        _refresh.RemoveCallbacksAndMessages(null);
        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (permissions.Zip(grantResults).Any(p => p.First == Android.Manifest.Permission.AccessFineLocation && p.Second == Permission.Granted))
            TrackingService.Start(this);
        else
            Toast.MakeText(this, "La localisation est nécessaire pour envoyer la position de l'équipe.", ToastLength.Long)!.Show();
    }

    // ----- Écrans -----

    private void Show()
    {
        _root.RemoveAllViews();
        if (Prefs.IsEnrolled)
            BuildDashboard();
        else
            BuildEnrollment(Prefs.ServerUrl, "");
    }

    private void BuildEnrollment(string server, string code)
    {
        AddTitle("Airsoft Planner");
        if (Prefs.Status.StartsWith('⛔'))
            Text(Prefs.Status, 15).SetTextColor(Color.Rgb(198, 40, 40));
        Text("Scannez le QR code d'enrôlement affiché par l'orga avec l'appareil photo du téléphone, ou saisissez l'adresse du PC de l'OP et le code de votre équipe.", 14, secondary: true);

        Label("Adresse du PC de l'OP");
        var serverInput = Input(server, "http://192.168.1.20:5055", Android.Text.InputTypes.TextVariationUri);
        Label("Code d'équipe");
        var codeInput = Input(code, "K7P-4QZ", Android.Text.InputTypes.TextFlagCapCharacters);
        Label("Nom du téléphone");
        var nameInput = Input(Prefs.DeviceName.Length > 0 ? Prefs.DeviceName : $"{Build.Manufacturer} {Build.Model}", "", Android.Text.InputTypes.ClassText);

        var enroll = PrimaryButton("S'enrôler");
        enroll.Click += async (_, _) =>
        {
            enroll.Enabled = false;
            await EnrollAsync(serverInput.Text ?? "", codeInput.Text ?? "", nameInput.Text ?? "");
            enroll.Enabled = true;
        };
    }

    private void BuildDashboard()
    {
        AddTitle(Prefs.Team);
        Text($"{Prefs.Faction}{(Prefs.Faction.Length > 0 ? " · " : "")}{Prefs.Operation}", 14, secondary: true);

        _status = Text("", 13, secondary: true);
        _toggle = PrimaryButton("");
        _toggle.Click += (_, _) =>
        {
            if (Prefs.IsTracking)
                TrackingService.Stop(this);
            else
                StartTracking();
            _refresh.PostDelayed(RefreshDashboard, 500);
        };

        Section("Mission");
        _mission = Text("", 15);

        Section("Radio et urgence");
        _comms = Text("", 15);
        _emergency = PrimaryButton("", Color.Rgb(198, 40, 40));
        _emergency.Click += (_, _) =>
        {
            var phone = (Prefs.LastResponse?.Comms ?? Prefs.EnrollComms)?.EmergencyPhone;
            if (!string.IsNullOrWhiteSpace(phone))
                StartActivity(new Intent(Intent.ActionDial, Android.Net.Uri.Parse("tel:" + phone.Replace(" ", ""))));
        };

        Section("Position");
        _ownPosition = Text("", 15);

        Section("Alliés");
        _map = new MapCanvasView(this) { ContentDescription = "Carte du terrain" };
        _root.AddView(_map, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _allies = Text("", 15);

        var leave = new Button(this) { Text = "Se désenrôler" };
        leave.Click += (_, _) =>
        {
            TrackingService.Stop(this);
            Prefs.Unenroll();
            Show();
        };
        _root.AddView(leave, Spaced());

        RefreshDashboard();
        if (!Prefs.IsTracking)
            StartTracking();
    }

    private void RefreshDashboard()
    {
        // Révoqué par l'orga pendant l'affichage du suivi : retour à l'écran d'enrôlement.
        if (_status is not null && !Prefs.IsEnrolled)
        {
            _status = null;
            Show();
            return;
        }

        if (_status is null)
            return;

        var response = Prefs.LastResponse;
        var format = response?.CoordinateFormat ?? CoordinateFormat.Utm;
        _status.Text = (Prefs.IsTracking ? "● Suivi actif" : "○ Suivi arrêté") +
                       $" · envoi toutes les {Prefs.IntervalSeconds} s\n{Prefs.Status}";
        _toggle!.Text = Prefs.IsTracking ? "Arrêter l'envoi de la position" : "Démarrer l'envoi de la position";

        _mission!.Text = response?.Mission is { } m
            ? $"{(m.IsCurrent ? "EN COURS" : "PROCHAINE")} : {m.Name}\n" +
              $"{m.Start.LocalDateTime.ToString("ddd HH:mm", French)} – {m.End.LocalDateTime:HH:mm}\n" +
              (m.Zone.Length > 0 ? $"Zone : {m.Zone}\n{m.ZoneCoordinates}\n" : "") +
              (m.Equipment.Length > 0 ? $"Matériel : {m.Equipment}\n" : "") +
              (m.Briefing.Length > 0 ? $"\n{m.Briefing}" : "")
            : "Aucune mission en cours ou à venir.";

        var comms = response?.Comms ?? Prefs.EnrollComms;
        _comms!.Text = comms is null
            ? "En attente du premier échange avec le PC de l'OP."
            : string.Join("\n", new[]
            {
                comms.Faction.Length > 0 ? $"Faction {comms.Faction} : {Freq(comms.FactionFrequency)}" : null,
                comms.OrgaFrequency.Length > 0 ? $"Orga : {comms.OrgaFrequency}" : null,
            }.OfType<string>().Concat(comms.Teams.Select(t => $"{t.Team}{(t.IsCommand ? " ★" : "")} : {Freq(t.Frequency)}")));
        _emergency!.Visibility = comms?.EmergencyPhone is { Length: > 0 } ? ViewStates.Visible : ViewStates.Gone;
        _emergency.Text = $"☎ Urgence orga : {comms?.EmergencyPhone}";

        GeoPoint? own = Prefs.LastLatitude is { } lat && Prefs.LastLongitude is { } lon ? new GeoPoint(lat, lon) : null;
        _ownPosition!.Text = own is { } p ? Coordinates.Format(p, format) : "En attente du GPS…";

        var mode = response?.ShareMode ?? AllyShareMode.None;
        var allies = response?.Allies ?? [];
        _allies!.Text = mode == AllyShareMode.None
            ? "Positions des alliés non partagées par l'orga."
            : allies.Count == 0
                ? "Aucune position alliée reçue pour le moment."
                : string.Join("\n\n", allies.Select(a =>
                    $"{a.Team} ({Freq(a.RadioFrequency)})\n{a.Coordinates}\nvu à {a.Time.LocalDateTime:HH:mm}"));

        UpdateMap(mode, response, own, allies);
    }

    private void UpdateMap(AllyShareMode mode, TrackResponse? response, GeoPoint? own, IReadOnlyList<AllyPosition> allies)
    {
        var file = TrackingService.MapFile(this);
        var visible = mode == AllyShareMode.Map && response?.Map is not null;
        _map!.Visibility = visible ? ViewStates.Visible : ViewStates.Gone;
        if (!visible)
            return;

        // Image rechargée seulement quand le fichier a changé (nouvelle carte envoyée par l'orga).
        var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0;
        if (stamp != _mapStamp)
        {
            _mapBitmap?.Recycle();
            _mapBitmap = stamp == 0 ? null : BitmapFactory.DecodeFile(file);
            _mapStamp = stamp;
            _map.RequestLayout();
        }

        var target = response!.Mission is { ZoneLatitude: { } zLat, ZoneLongitude: { } zLon } ? new GeoPoint(zLat, zLon) : (GeoPoint?)null;
        _map.Update(_mapBitmap, response.Map, own, allies, target);
    }

    private void ScheduleRefresh()
    {
        _refresh.RemoveCallbacksAndMessages(null);
        _refresh.PostDelayed(() =>
        {
            RefreshDashboard();
            ScheduleRefresh();
        }, 2000);
    }

    // ----- Actions -----

    private void HandleLink(Intent? intent)
    {
        if (intent?.Data?.ToString() is not { } link || !EnrollmentLink.TryParse(link, out var server, out var code))
            return;

        if (Prefs.IsEnrolled)
        {
            Toast.MakeText(this, "Ce téléphone est déjà enrôlé : désenrôlez-le d'abord pour changer d'équipe.", ToastLength.Long)!.Show();
            return;
        }

        _root.RemoveAllViews();
        BuildEnrollment(server, EnrollmentCodes.Format(code));
        _ = EnrollAsync(server, code, Prefs.DeviceName.Length > 0 ? Prefs.DeviceName : $"{Build.Manufacturer} {Build.Model}");
    }

    private async Task EnrollAsync(string server, string code, string deviceName)
    {
        try
        {
            EnrollResponse? response;
            try
            {
                response = await ServerApi.EnrollAsync(server, EnrollmentCodes.Normalize(code), deviceName.Trim());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Adresse du package périmée (IP du PC changée, pas d'Internet pour le nom DynDNS) : recherche sur le Wi-Fi.
                Toast.MakeText(this, "PC de l'OP injoignable : recherche sur le Wi-Fi…", ToastLength.Short)!.Show();
                if (await ServerApi.DiscoverAsync(null, server) is not { } found)
                    throw;
                server = found;
                response = await ServerApi.EnrollAsync(server, EnrollmentCodes.Normalize(code), deviceName.Trim());
            }

            if (response is null)
            {
                Toast.MakeText(this, "Code inconnu : vérifiez-le auprès de l'orga.", ToastLength.Long)!.Show();
                return;
            }

            Prefs.ServerUrl = ServerApi.Normalize(server);
            Prefs.DeviceName = deviceName.Trim();
            Prefs.Token = response.Token;
            Prefs.Team = response.Team;
            Prefs.Operation = response.Operation;
            Prefs.Faction = response.Faction;
            Prefs.IntervalSeconds = response.IntervalSeconds;
            Prefs.EnrollComms = response.Comms;
            Prefs.OperationId = response.OperationId;
            Prefs.Status = "Enrôlé : démarrage du suivi.";
            Show();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            Toast.MakeText(this, $"PC de l'OP injoignable (même Wi-Fi ? serveur actif ?) : {ex.Message}", ToastLength.Long)!.Show();
        }
    }

    private void StartTracking()
    {
        var needed = new List<string> { Android.Manifest.Permission.AccessFineLocation, Android.Manifest.Permission.AccessCoarseLocation };
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            needed.Add(Android.Manifest.Permission.PostNotifications);
        var missing = needed.Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (missing.Length > 0)
            RequestPermissions(missing, 1);
        else
            TrackingService.Start(this);
    }

    // ----- Petits éléments d'interface -----

    private static string Freq(string frequency) => frequency.Length > 0 ? frequency : "—";

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density);

    private LinearLayout.LayoutParams Spaced(int top = 8) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(top) };

    private void AddTitle(string text) => _root.AddView(new TextView(this) { Text = text, TextSize = 26, Typeface = Typeface.DefaultBold });

    private void Section(string text) =>
        _root.AddView(new TextView(this) { Text = text.ToUpperInvariant(), TextSize = 13, Typeface = Typeface.DefaultBold, LetterSpacing = 0.08f }, Spaced(20));

    private void Label(string text) => _root.AddView(new TextView(this) { Text = text, TextSize = 13 }, Spaced(12));

    private TextView Text(string text, float size, bool secondary = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        if (secondary)
            view.Alpha = 0.7f;
        view.SetTextIsSelectable(true);
        _root.AddView(view, Spaced(4));
        return view;
    }

    private EditText Input(string text, string hint, Android.Text.InputTypes type)
    {
        var input = new EditText(this) { Text = text, Hint = hint, InputType = type };
        input.SetSingleLine(true);
        _root.AddView(input, Spaced(2));
        return input;
    }

    private Button PrimaryButton(string text, Color? color = null)
    {
        var button = new Button(this) { Text = text };
        if (color is { } c)
        {
            button.SetBackgroundColor(c);
            button.SetTextColor(Color.White);
        }

        _root.AddView(button, Spaced(12));
        return button;
    }

    /// <summary>Marges du contenu ajustées à la barre d'état et à la barre de navigation du téléphone.</summary>
    private sealed class InsetsListener(View content, int padding, int bottomPadding) : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            int top, bottom;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                (top, bottom) = (bars.Top, bars.Bottom);
            }
            else
            {
                (top, bottom) = (insets.SystemWindowInsetTop, insets.SystemWindowInsetBottom);
            }

            content.SetPadding(padding, top + padding, padding, bottom + bottomPadding);
            return insets;
        }
    }
}
