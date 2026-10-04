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
using AirsoftPlanner.Core.Localization;

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
    private static CultureInfo French => AirsoftPlanner.Core.Localization.L.Culture;
    private readonly Handler _refresh = new(Looper.MainLooper!);
    private LinearLayout _root = null!;

    // Conteneur où les éléments sont ajoutés pendant la construction d'un écran.
    private ViewGroup _target = null!;

    // Onglets du suivi : QG (en jeu) et ORGA (organisation).
    private LinearLayout? _qgPane;
    private LinearLayout? _orgaPane;
    private Button? _qgTab;
    private Button? _orgaTab;
    private bool _orgaSelected;

    // Écran de suivi
    private TextView? _status;
    private Button? _toggle;
    private TextView? _level;
    private TextView? _phase;
    private EditText? _reportText;
    private ImageView? _reportPreview;
    private Button? _reportRemovePhoto;
    private byte[]? _reportPhoto;
    // Fenêtre « Nouveau message » (QG ou orga), ouverte par le bouton ✉ en haut de l'écran.
    private bool _composing;
    private MessageSender _composeRecipient = MessageSender.Hq;
    private RadioButton? _toHq;
    private const int CameraRequest = 41;
    private const int GalleryRequest = 42;
    private readonly List<View> _gameParts = [];
    private View? _radioPart;
    private TextView? _mission;
    private LinearLayout? _messages;
    private string _messagesSignature = "";
    private readonly Dictionary<Guid, Bitmap> _photos = [];
    private Button? _history;
    private bool _showHistory;
    private TextView? _orgaComms;
    private LinearLayout? _orgaMessages;
    private TextView? _comms;
    private Button? _emergency;
    private TextView? _allies;
    private TextView? _points;
    private TextView? _ownPosition;
    private MapCanvasView? _map;
    private Bitmap? _mapBitmap;
    private long _mapStamp;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Prefs.ApplyLanguage();
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
        // Écran recréé (passage jour/nuit, rotation) : le lien d'enrôlement d'origine a déjà été traité.
        if (savedInstanceState is null)
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
            Toast.MakeText(this, L.T("la_localisation_est_necessaire_pour_envoyer_la_p"), ToastLength.Long)!.Show();
    }

    // ----- Écrans -----

    /// <summary>Titre de l'application, en tête de tous les écrans.</summary>
    public const string AppTitle = "AIRSOFT PLANNER · FIELD LINK";

    private void Show()
    {
        _root.RemoveAllViews();
        _target = _root;
        _messagesSignature = "";
        _toggle = null;
        _status = null;
        var title = new TextView(this) { Text = AppTitle, TextSize = 11, LetterSpacing = 0.15f, Gravity = GravityFlags.Center };
        title.SetTypeface(null, TypefaceStyle.Bold);
        title.Alpha = 0.6f;
        _root.AddView(title, Spaced(0));
        AddHeader();
        if (Prefs.IsEnrolled && _composing)
            BuildCompose();
        else if (Prefs.IsEnrolled)
            BuildDashboard();
        else
            BuildEnrollment(Prefs.ServerUrl, "");
        ApplyNightMode();
    }

    /// <summary>Retour du téléphone : la fenêtre de message se ferme (le brouillon est abandonné).</summary>
    public override void OnBackPressed()
    {
        if (_composing)
        {
            _composing = false;
            Show();
            return;
        }
        base.OnBackPressed();
    }

    // ----- Fenêtre « Nouveau message » -----

    private void OpenCompose()
    {
        // Destinataire proposé : celui de l'onglet affiché.
        _composeRecipient = _orgaSelected ? MessageSender.Orga : MessageSender.Hq;
        _composing = true;
        _reportPhoto = null;
        Show();
    }

    private void BuildCompose()
    {
        AddTitle(L.T("nouveau_message"));
        Text($"{Prefs.Team} · {Prefs.Operation}", 13, secondary: true);

        Label(L.T("destinataire"));
        var recipients = new RadioGroup(this) { Orientation = Orientation.Horizontal };
        _toHq = new RadioButton(this) { Text = L.T("destinataire_qg"), Id = View.GenerateViewId() };
        var toOrga = new RadioButton(this) { Text = L.T("destinataire_orga"), Id = View.GenerateViewId() };
        recipients.AddView(_toHq);
        recipients.AddView(toOrga);
        recipients.Check(_composeRecipient == MessageSender.Hq ? _toHq.Id : toOrga.Id);
        recipients.CheckedChange += (_, e) => _composeRecipient = e.CheckedId == _toHq.Id ? MessageSender.Hq : MessageSender.Orga;
        _target.AddView(recipients, Spaced(4));

        _reportText = Input("", L.T("votre_message"), Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagMultiLine
            | Android.Text.InputTypes.TextFlagCapSentences);
        _reportText.SetMinLines(3);
        var photoRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var camera = new Button(this) { Text = L.T("bouton_prendre_une_photo"), TextSize = 13 };
            camera.Click += (_, _) => TakePhoto();
            photoRow.AddView(camera, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        }
        var gallery = new Button(this) { Text = L.T("bouton_choisir_une_photo"), TextSize = 13 };
        gallery.Click += (_, _) => StartActivityForResult(new Intent(Intent.ActionGetContent).SetType("image/*"), GalleryRequest);
        photoRow.AddView(gallery, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _target.AddView(photoRow, Spaced(4));
        _reportPreview = new ImageView(this) { Visibility = ViewStates.Gone, ContentDescription = L.T("photo_jointe") };
        _reportPreview.SetAdjustViewBounds(true);
        _reportPreview.SetMaxHeight(Dp(220));
        _target.AddView(_reportPreview, Spaced(4));
        _reportRemovePhoto = new Button(this) { Text = L.T("retirer_la_photo"), TextSize = 12, Visibility = ViewStates.Gone };
        _reportRemovePhoto.Click += (_, _) => SetReportPhoto(null);
        _target.AddView(_reportRemovePhoto, Spaced(2));
        SetReportPhoto(_reportPhoto);

        var send = PrimaryButton(L.T("envoyer"));
        send.Click += async (_, _) => await SendReportAsync();
        var cancel = new Button(this) { Text = L.T("annuler") };
        cancel.Click += (_, _) =>
        {
            _composing = false;
            Show();
        };
        _target.AddView(cancel, Spaced(8));
    }

    // ----- Mode nuit -----

    // Rouge sombre : préserve la vision de nuit et reste discret sur le terrain.
    private static readonly Color NightText = Color.Rgb(200, 30, 30);
    private static readonly Color NightBackground = Color.Black;

    private void AddHeader()
    {
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        if (Prefs.IsEnrolled)
        {
            // Envoi de la position : icône ▶ / ⏸ (le libellé complet sert à l'accessibilité).
            _toggle = new Button(this) { TextSize = 20, Text = Prefs.IsTracking ? "⏸" : "▶" };
            _toggle.ContentDescription = Prefs.IsTracking ? L.T("arreter_l_envoi_de_la_position") : L.T("demarrer_l_envoi_de_la_position");
            _toggle.Click += (_, _) =>
            {
                if (Prefs.IsTracking)
                    TrackingService.Stop(this);
                else
                    StartTracking();
                _refresh.PostDelayed(RefreshDashboard, 500);
            };
            header.AddView(_toggle, new LinearLayout.LayoutParams(Dp(64), ViewGroup.LayoutParams.WrapContent));
            var write = new Button(this) { Text = "✉", TextSize = 20, ContentDescription = L.T("ecrire_un_message") };
            write.Click += (_, _) => OpenCompose();
            header.AddView(write, new LinearLayout.LayoutParams(Dp(64), ViewGroup.LayoutParams.WrapContent));
        }

        header.AddView(new View(this), new LinearLayout.LayoutParams(0, 1, 1));
        var language = new Button(this) { Text = "🌐", TextSize = 16, ContentDescription = L.T("langue") };
        language.Click += (_, _) =>
        {
            var languages = L.Languages;
            new AlertDialog.Builder(this)
                .SetTitle(L.T("langue"))!
                .SetItems(languages.Select(l => l.Name).ToArray(), (_, e) =>
                {
                    Prefs.Language = languages[e.Which].Code;
                    Prefs.ApplyLanguage();
                    Recreate();
                })!
                .Show();
        };
        header.AddView(language, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        var toggle = new Button(this) { Text = Prefs.NightMode ? L.T("mode_jour") : L.T("mode_nuit"), TextSize = 12 };
        toggle.Click += (_, _) =>
        {
            Prefs.NightMode = !Prefs.NightMode;
            // Les couleurs par défaut du thème sont rétablies en recréant l'écran.
            Recreate();
        };
        header.AddView(toggle, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
        _root.AddView(header, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
    }

    /// <summary>Texte rouge sur fond noir, carte assombrie en rouge, luminosité de l'écran au minimum.</summary>
    private void ApplyNightMode()
    {
        var attributes = Window!.Attributes!;
        attributes.ScreenBrightness = Prefs.NightMode ? 0.01f : WindowManagerLayoutParams.BrightnessOverrideNone;
        Window.Attributes = attributes;
        if (_map is not null)
            _map.NightMode = Prefs.NightMode;
        if (!Prefs.NightMode)
            return;

        Window.DecorView.SetBackgroundColor(NightBackground);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            Window.InsetsController?.SetSystemBarsAppearance(0, (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars));
        Paint(_root);
    }

    private void Paint(ViewGroup group)
    {
        for (var i = 0; i < group.ChildCount; i++)
        {
            switch (group.GetChildAt(i))
            {
                case Button button:
                    var isEmergency = button == _emergency;
                    button.SetBackgroundColor(isEmergency ? Color.Rgb(70, 0, 0) : Color.Rgb(28, 4, 4));
                    button.SetTextColor(isEmergency ? Color.Rgb(255, 82, 82) : NightText);
                    break;
                case EditText input:
                    input.SetTextColor(NightText);
                    input.SetHintTextColor(Color.Rgb(90, 20, 20));
                    input.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Color.Rgb(110, 20, 20));
                    break;
                case TextView text:
                    text.SetTextColor(NightText);
                    break;
                case ViewGroup child:
                    Paint(child);
                    break;
            }
        }
    }

    private void BuildEnrollment(string server, string code)
    {
        AddTitle(L.T("airsoft_planner"));
        if (Prefs.Status.StartsWith('⛔'))
            Text(Prefs.Status, 15).SetTextColor(Color.Rgb(198, 40, 40));
        Text(L.T("scannez_le_qr_code_d_enrolement_affiche_par_l_or"), 14, secondary: true);

        Label(L.T("adresse_du_pc_de_l_op"));
        var serverInput = Input(server, "http://192.168.1.20:5055", Android.Text.InputTypes.TextVariationUri);
        Label(L.T("code_d_equipe_2"));
        var codeInput = Input(code, "K7P-4QZ", Android.Text.InputTypes.TextFlagCapCharacters);
        Label(L.T("nom_du_telephone"));
        var nameInput = Input(Prefs.DeviceName.Length > 0 ? Prefs.DeviceName : $"{Build.Manufacturer} {Build.Model}", "", Android.Text.InputTypes.ClassText);

        var enroll = PrimaryButton(L.T("s_enroler"));
        enroll.Click += async (_, _) =>
        {
            enroll.Enabled = false;
            await EnrollAsync(serverInput.Text ?? "", codeInput.Text ?? "", nameInput.Text ?? "");
            enroll.Enabled = true;
        };
        About();
    }

    private void BuildDashboard()
    {
        AddTitle(Prefs.Team);
        Text($"{Prefs.Faction}{(Prefs.Faction.Length > 0 ? " · " : "")}{Prefs.Operation}", 14, secondary: true);

        _status = Text("", 13, secondary: true);

        // Phase de la partie annoncée par l'orga (début, pause, fin) : visible quel que soit l'onglet.
        _phase = new TextView(this) { TextSize = 15, Gravity = GravityFlags.Center };
        _phase.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        _phase.SetTypeface(null, TypefaceStyle.Bold);
        _root.AddView(_phase, Spaced(8));

        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _qgTab = new Button(this);
        _orgaTab = new Button(this);
        _qgTab.Click += (_, _) => SelectTab(orga: false);
        _orgaTab.Click += (_, _) => SelectTab(orga: true);
        tabs.AddView(_qgTab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        tabs.AddView(_orgaTab, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        _root.AddView(tabs, Spaced(16));

        // ----- Onglet QG : le jeu (contenu limité par le niveau de difficulté choisi par l'orga) -----
        _qgPane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _root.AddView(_qgPane);
        _target = _qgPane;

        Section(L.T("niveau_de_difficulte"));
        _level = Text("", 14);

        _gameParts.Clear();
        _target = Part(_gameParts);
        Section(L.T("mission_2"));
        _mission = Text("", 15);

        // Ordres du QG (en jeu) : groupés par mission, historique complet à la demande.
        Section(L.T("messages_du_qg"));
        _messages = List();
        _history = new Button(this) { TextSize = 13 };
        _history.Click += (_, _) =>
        {
            _showHistory = !_showHistory;
            RefreshDashboard();
        };
        _target.AddView(_history, Spaced(4));

        // Radio : seule partie conservée en Difficile.
        _target = Part(null);
        _radioPart = _target;
        Section(L.T("radio"));
        _comms = Text("", 15);

        _target = Part(_gameParts);
        Section(L.T("points_d_interet"));
        _points = Text("", 15);

        Section(L.T("position"));
        _ownPosition = Text("", 15);

        Section(L.T("allies"));
        _map = new MapCanvasView(this) { ContentDescription = L.T("carte_du_terrain") };
        _target.AddView(_map, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _allies = Text("", 15);

        // ----- Onglet ORGA : l'organisation (hors jeu) -----
        _orgaPane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _root.AddView(_orgaPane);
        _target = _orgaPane;

        Section(L.T("contacts_de_l_orga"));
        _orgaComms = Text("", 15);
        _emergency = PrimaryButton("", Color.Rgb(198, 40, 40));
        _emergency.Click += (_, _) =>
        {
            var phone = (Prefs.LastResponse?.Comms ?? Prefs.EnrollComms)?.EmergencyPhone;
            if (!string.IsNullOrWhiteSpace(phone))
                StartActivity(new Intent(Intent.ActionDial, Android.Net.Uri.Parse("tel:" + phone.Replace(" ", ""))));
        };


        Section(L.T("messages_de_l_orga"));
        _orgaMessages = List();

        var leave = new Button(this) { Text = L.T("se_desenroler") };
        leave.Click += (_, _) =>
        {
            TrackingService.Stop(this);
            Prefs.Unenroll();
            Show();
        };
        _target.AddView(leave, Spaced(24));
        About();
        _target = _root;

        SelectTab(_orgaSelected);
        if (!Prefs.IsTracking)
            StartTracking();
    }

    /// <summary>Bandeau de la phase de la partie : discret pendant le jeu, très visible en pause et à la fin.</summary>
    private void RefreshPhase(TrackResponse? response)
    {
        if (response is null)
        {
            _phase!.Visibility = ViewStates.Gone;
            return;
        }
        var phase = response.Phase;
        var since = response.PhaseSince is { } at ? L.F("depuis_x", at.LocalDateTime.ToString("HH:mm")) : "";
        _phase!.Visibility = ViewStates.Visible;
        _phase.Text = phase switch
        {
            AirsoftPlanner.Core.Domain.GamePhase.Paused => $"⏸ {AirsoftPlanner.Core.Domain.GamePhases.Label(phase).ToUpper(L.Culture)}{since}\n{L.T("annonce_jeu_en_pause")}",
            AirsoftPlanner.Core.Domain.GamePhase.Ended => $"⏹ {AirsoftPlanner.Core.Domain.GamePhases.Label(phase).ToUpper(L.Culture)}{since}\n{L.T("annonce_fin_de_partie")}",
            _ => $"{AirsoftPlanner.Core.Domain.GamePhases.Symbol(phase)} {AirsoftPlanner.Core.Domain.GamePhases.Label(phase)}{since}",
        };
        _phase.TextSize = phase is AirsoftPlanner.Core.Domain.GamePhase.Paused or AirsoftPlanner.Core.Domain.GamePhase.Ended ? 18 : 13;
        var (background, text) = phase switch
        {
            AirsoftPlanner.Core.Domain.GamePhase.Paused => (Color.Rgb(239, 108, 0), Color.White),
            AirsoftPlanner.Core.Domain.GamePhase.Ended => (Color.Rgb(198, 40, 40), Color.White),
            AirsoftPlanner.Core.Domain.GamePhase.Running => (Color.Rgb(46, 125, 50), Color.White),
            _ => (Color.Rgb(120, 120, 120), Color.White),
        };
        if (Prefs.NightMode)
            (background, text) = (Color.Rgb(40, 0, 0), phase is AirsoftPlanner.Core.Domain.GamePhase.Paused or AirsoftPlanner.Core.Domain.GamePhase.Ended
                ? Color.Rgb(255, 82, 82) : NightText);
        _phase.SetBackgroundColor(background);
        _phase.SetTextColor(text);
    }

    // ----- Message vers l'orga -----

    private void TakePhoto()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return;
        // La photo est enregistrée dans la galerie (Images/AirsoftPlanner), puis réduite pour l'envoi.
        var values = new ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, $"AirsoftPlanner_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "image/jpeg");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Pictures/AirsoftPlanner");
        var uri = ContentResolver!.Insert(Android.Provider.MediaStore.Images.Media.ExternalContentUri!, values);
        if (uri is null)
            return;
        Prefs.CameraUri = uri.ToString()!;
        var intent = new Intent(Android.Provider.MediaStore.ActionImageCapture);
        intent.PutExtra(Android.Provider.MediaStore.ExtraOutput, uri);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission);
        try
        {
            StartActivityForResult(intent, CameraRequest);
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(this, L.T("aucun_appareil_photo"), ToastLength.Short)!.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        var uri = requestCode switch
        {
            CameraRequest when Prefs.CameraUri.Length > 0 => Android.Net.Uri.Parse(Prefs.CameraUri),
            GalleryRequest => data?.Data,
            _ => null,
        };
        if (requestCode == CameraRequest)
            Prefs.CameraUri = "";
        if (resultCode != Result.Ok || uri is null)
            return;
        try
        {
            SetReportPhoto(Outbox.Shrink(this, uri));
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.SecurityException)
        {
            Toast.MakeText(this, L.T("photo_illisible"), ToastLength.Short)!.Show();
        }
    }

    private void SetReportPhoto(byte[]? photo)
    {
        _reportPhoto = photo;
        if (_reportPreview is null)
            return;
        _reportPreview.Visibility = photo is null ? ViewStates.Gone : ViewStates.Visible;
        _reportRemovePhoto!.Visibility = _reportPreview.Visibility;
        _reportPreview.SetImageBitmap(photo is null ? null : BitmapFactory.DecodeByteArray(photo, 0, photo.Length));
    }

    private async Task SendReportAsync()
    {
        var text = _reportText?.Text?.Trim() ?? "";
        if (text.Length == 0 && _reportPhoto is null)
        {
            Toast.MakeText(this, L.T("ecrivez_un_message_ou_joignez_une_photo"), ToastLength.Short)!.Show();
            return;
        }
        var recipient = _composeRecipient;
        Outbox.Add(text, _reportPhoto, recipient);
        _reportPhoto = null;
        _composing = false;
        // Retour sur l'onglet du destinataire, où le message apparaît.
        _orgaSelected = recipient == MessageSender.Orga;
        Show();
        var sent = await Outbox.SendPendingAsync();
        Toast.MakeText(this, sent ? L.T(recipient == MessageSender.Hq ? "message_envoye_au_qg" : "message_envoye_a_l_orga") : L.T("message_en_attente_du_reseau"),
            ToastLength.Short)!.Show();
        _messagesSignature = "";
        RefreshDashboard();
    }

    /// <summary>Partie de l'onglet QG, masquable selon le niveau de difficulté.</summary>
    private LinearLayout Part(List<View>? group)
    {
        var part = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _qgPane!.AddView(part);
        group?.Add(part);
        return part;
    }

    /// <summary>
    /// Niveau de difficulté rappelé en tête de l'onglet QG, avec ce qu'il permet ; les parties non autorisées sont masquées
    /// (le PC ne les envoie d'ailleurs pas).
    /// </summary>
    private void RefreshLevel(TrackResponse? response)
    {
        var level = response?.Difficulty ?? HqDifficulty.Easy;
        _level!.Text = response is null
            ? L.T("en_attente_du_premier_echange_avec_le_pc_de_l_op")
            : $"{HqDifficultyRules.Label(level)}\n{HqDifficultyRules.Explanation(level)}";
        var game = response is null || HqDifficultyRules.SharesGame(level);
        foreach (var part in _gameParts)
            part.Visibility = game ? ViewStates.Visible : ViewStates.Gone;
        _radioPart!.Visibility = response is null || HqDifficultyRules.SharesRadio(level) ? ViewStates.Visible : ViewStates.Gone;
    }

    private void SelectTab(bool orga)
    {
        _orgaSelected = orga;
        _qgPane!.Visibility = orga ? ViewStates.Gone : ViewStates.Visible;
        _orgaPane!.Visibility = orga ? ViewStates.Visible : ViewStates.Gone;
        RefreshDashboard();
    }

    /// <summary>Onglet affiché en gras ; l'autre indique ses messages non lus.</summary>
    private void RefreshTabs(IReadOnlyList<PhoneMessage> archive)
    {
        var now = DateTimeOffset.Now;
        if (_orgaSelected)
            Prefs.OrgaSeenAt = now;
        else
            Prefs.HqSeenAt = now;
        var unreadHq = archive.Count(m => m.Sender == MessageSender.Hq && m.SentAt > Prefs.HqSeenAt);
        var unreadOrga = archive.Count(m => m.Sender == MessageSender.Orga && m.SentAt > Prefs.OrgaSeenAt);
        _qgTab!.Text = unreadHq > 0 ? $"{L.T("onglet_qg")} ({unreadHq})" : L.T("onglet_qg");
        _orgaTab!.Text = unreadOrga > 0 ? $"{L.T("onglet_orga")} ({unreadOrga})" : L.T("onglet_orga");
        _qgTab.SetTypeface(null, _orgaSelected ? TypefaceStyle.Normal : TypefaceStyle.Bold);
        _orgaTab.SetTypeface(null, _orgaSelected ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        _qgTab.Alpha = _orgaSelected ? 0.6f : 1;
        _orgaTab.Alpha = _orgaSelected ? 1 : 0.6f;
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
        RefreshLevel(response);
        RefreshPhase(response);
        _status.Text = (Prefs.IsTracking ? L.T("suivi_actif") : L.T("suivi_arrete")) +
                       L.F("envoi_toutes_les_x_s_x", Prefs.IntervalSeconds, Prefs.Status);
        _toggle!.Text = Prefs.IsTracking ? "⏸" : "▶";
        _toggle.ContentDescription = Prefs.IsTracking ? L.T("arreter_l_envoi_de_la_position") : L.T("demarrer_l_envoi_de_la_position");

        _mission!.Text = response?.Mission is { } m
            ? $"MISSION : {m.Name}\n" +
              $"{m.Start.LocalDateTime.ToString("ddd HH:mm", French)} – {m.End.LocalDateTime:HH:mm}\n" +
              (m.Zone.Length > 0 ? L.F("zone_x_x", m.Zone, m.ZoneCoordinates) : "") +
              (m.Equipment.Length > 0 ? L.F("materiel_x", m.Equipment) : "") +
              (m.Briefing.Length > 0 ? $"\n{m.Briefing}" : "")
            : response is null ? L.T("en_attente_du_premier_echange_avec_le_pc_de_l_op")
            : L.T("aucune_mission_diffusee_par_l_orga_attendez_les");

        var archive = Prefs.MessageArchive;
        RefreshTabs(archive);
        var groups = MessageHistory.Group(archive.Where(x => x.Sender == MessageSender.Hq));
        _history!.Visibility = groups.Count > 1 ? ViewStates.Visible : ViewStates.Gone;
        _history.Text = _showHistory ? L.T("masquer_l_historique") : L.F("historique_des_messages_x_mission_s_precedente_s", groups.Count - 1);
        RenderMessages(archive, groups);

        var comms = response?.Comms ?? Prefs.EnrollComms;
        _comms!.Text = comms is null
            ? L.T("en_attente_du_premier_echange_avec_le_pc_de_l_op")
            : string.Join("\n", new[]
            {
                comms.Faction.Length > 0 ? L.F("faction_x_x", comms.Faction, Freq(comms.FactionFrequency)) : null,
            }.OfType<string>().Concat(comms.Teams.Select(t => $"{t.Team}{(t.IsCommand ? " ★" : "")} : {Freq(t.Frequency)}")));
        _orgaComms!.Text = comms is null ? "" : L.F("frequence_orga_x", Freq(comms.OrgaFrequency));
        _emergency!.Visibility = comms?.EmergencyPhone is { Length: > 0 } ? ViewStates.Visible : ViewStates.Gone;
        _emergency.Text = L.F("urgence_orga_x", comms?.EmergencyPhone);

        var points = response?.Points ?? [];
        _points!.Text = points.Count == 0
            ? L.T("aucun_point_communique_par_l_orga")
            : string.Join("\n\n", points.Select(p =>
                $"{p.Symbol} {p.Name} — {p.Category}\n{p.Coordinates}" + (p.Description.Length > 0 ? $"\n{p.Description}" : "")));

        GeoPoint? own = Prefs.LastLatitude is { } lat && Prefs.LastLongitude is { } lon ? new GeoPoint(lat, lon) : null;
        _ownPosition!.Text = own is { } p ? Coordinates.Format(p, format) : L.T("en_attente_du_gps");

        var mode = response?.ShareMode ?? AllyShareMode.None;
        var allies = response?.Allies ?? [];
        _allies!.Text = mode == AllyShareMode.None
            ? L.T("positions_des_allies_non_partagees_par_l_orga")
            : allies.Count == 0
                ? L.T("aucune_position_alliee_recue_pour_le_moment")
                : string.Join("\n\n", allies.Select(a =>
                    L.F("x_x_x_vu_a_x", a.Team, Freq(a.RadioFrequency), a.Coordinates, a.Time.LocalDateTime)));

        UpdateMap(mode, response, own, allies);
    }

    /// <summary>
    /// Messages du QG (groupés par mission) et de l'orga, avec leurs photos. Reconstruits seulement quand
    /// l'historique change (nouveau message, photo téléchargée, historique affiché ou masqué).
    /// </summary>
    private void RenderMessages(IReadOnlyList<PhoneMessage> archive, IReadOnlyList<MessageGroup> groups)
    {
        var photosReady = archive.Count(m => m.HasPhoto && File.Exists(TrackingService.PhotoFile(this, m.Id)));
        var outbox = Outbox.All;
        var signature = $"{_showHistory}|{archive.Count}|{archive.LastOrDefault()?.Id}|{photosReady}|{outbox.Count}|{outbox.Count(r => r.Delivered)}";
        if (signature == _messagesSignature)
            return;
        _messagesSignature = signature;

        _messages!.RemoveAllViews();
        if (groups.Count == 0)
            AddLine(_messages, L.T("aucun_message_du_qg"), 15);
        var toHq = outbox.Where(r => r.Recipient == MessageSender.Hq).OrderByDescending(r => r.SentAt).ToList();
        if (toHq.Count > 0)
        {
            AddLine(_messages, L.T("vos_messages_au_qg"), 14, bold: true);
            foreach (var report in _showHistory ? toHq : toHq.Take(3))
                AddSentReport(_messages, report);
        }
        foreach (var group in _showHistory ? groups : groups.Take(1))
        {
            AddLine(_messages, group.Mission.Length > 0 ? L.F("mission_x_2", group.Mission) : L.T("hors_mission"), 14, bold: true);
            foreach (var message in group.Messages.Reverse())
                AddMessage(_messages, message, $"{message.SentAt.LocalDateTime:HH:mm} · {message.Audience}");
        }

        var orga = archive.Where(x => x.Sender == MessageSender.Orga).ToList();
        var toOrga = outbox.Where(r => r.Recipient == MessageSender.Orga).ToList();
        _orgaMessages!.RemoveAllViews();
        if (orga.Count == 0 && toOrga.Count == 0)
            AddLine(_orgaMessages, L.T("aucun_message_de_l_orga"), 15);
        // Reçus et envoyés mélangés par date, le plus récent en premier.
        var items = orga.Select(m => (At: m.SentAt, Received: m, Sent: (OutgoingReport?)null))
            .Concat(toOrga.Select(r => (At: r.SentAt, Received: (PhoneMessage?)null, Sent: (OutgoingReport?)r)))
            .OrderByDescending(i => i.At);
        foreach (var item in items)
        {
            if (item.Received is { } message)
                AddMessage(_orgaMessages, message, $"{message.SentAt.LocalDateTime.ToString("ddd HH:mm", French)} · {message.Audience}");
            else if (item.Sent is { } report)
                AddSentReport(_orgaMessages, report);
        }

        if (Prefs.NightMode)
        {
            Paint(_messages);
            Paint(_orgaMessages);
        }
    }

    private void AddSentReport(LinearLayout list, OutgoingReport report)
    {
        var state = report.Delivered ? L.T("message_recu") : L.T("en_attente_du_reseau");
        var header = $"{report.SentAt.LocalDateTime.ToString("ddd HH:mm", French)} · "
                     + $"{L.T(report.Recipient == MessageSender.Hq ? "vous_vers_qg" : "vous_vers_orga")} · {state}";
        AddLine(list, report.Text.Length > 0 ? $"{header}\n{report.Text}" : header, 15);
        if (report.HasPhoto && File.Exists(Outbox.PhotoFile(report.Id)) && BitmapFactory.DecodeFile(Outbox.PhotoFile(report.Id)) is { } bitmap)
        {
            var image = new ImageView(this) { ContentDescription = L.T("photo_jointe") };
            image.SetAdjustViewBounds(true);
            image.SetMaxHeight(Dp(160));
            image.SetImageBitmap(bitmap);
            list.AddView(image, Spaced(4));
        }
    }

    private void AddMessage(LinearLayout list, PhoneMessage message, string header)
    {
        AddLine(list, message.Text.Length > 0 ? $"{header}\n{message.Text}" : header, 15);
        if (!message.HasPhoto)
            return;

        var file = TrackingService.PhotoFile(this, message.Id);
        if (!_photos.TryGetValue(message.Id, out var bitmap) && File.Exists(file) && BitmapFactory.DecodeFile(file) is { } decoded)
            _photos[message.Id] = bitmap = decoded;
        if (bitmap is null)
        {
            AddLine(list, L.T("photo_en_cours_de_telechargement"), 13);
            return;
        }

        var image = new ImageView(this) { ContentDescription = L.T("photo_jointe") };
        image.SetAdjustViewBounds(true);
        image.SetImageBitmap(bitmap);
        if (Prefs.NightMode)
            image.SetColorFilter(new ColorMatrixColorFilter(new ColorMatrix([0.12f, 0.24f, 0.04f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0])));
        list.AddView(image, Spaced(4));
    }

    private void AddLine(LinearLayout list, string text, float size, bool bold = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        if (bold)
            view.SetTypeface(null, TypefaceStyle.Bold);
        view.SetTextIsSelectable(true);
        list.AddView(view, Spaced(bold ? 12 : 6));
    }

    private LinearLayout List()
    {
        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _target.AddView(list, Spaced(0));
        return list;
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
        _map.Update(_mapBitmap, response.Map, own, allies, target, response.Points ?? []);
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
            Toast.MakeText(this, L.T("ce_telephone_est_deja_enrole_desenrolez_le_d_abo"), ToastLength.Long)!.Show();
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
                Toast.MakeText(this, L.T("pc_de_l_op_injoignable_recherche_sur_le_wi_fi"), ToastLength.Short)!.Show();
                if (await ServerApi.DiscoverAsync(null, server) is not { } found)
                    throw;
                server = found;
                response = await ServerApi.EnrollAsync(server, EnrollmentCodes.Normalize(code), deviceName.Trim());
            }

            if (response is null)
            {
                Toast.MakeText(this, L.T("code_inconnu_verifiez_le_aupres_de_l_orga"), ToastLength.Long)!.Show();
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
            Prefs.EnrolledAt = DateTimeOffset.Now;
            Prefs.Status = L.T("enrole_demarrage_du_suivi");
            Show();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            Toast.MakeText(this, L.F("pc_de_l_op_injoignable_meme_wi_fi_serveur_actif", ex.Message), ToastLength.Long)!.Show();
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

    private void AddTitle(string text) => _target.AddView(new TextView(this) { Text = text, TextSize = 26, Typeface = Typeface.DefaultBold });

    private void Section(string text) =>
        _target.AddView(new TextView(this) { Text = text.ToUpperInvariant(), TextSize = 13, Typeface = Typeface.DefaultBold, LetterSpacing = 0.08f }, Spaced(20));

    /// <summary>À propos : version et adresse du projet, nouvelle version disponible sur GitHub.</summary>
    private void About()
    {
        var version = PackageManager?.GetPackageInfo(PackageName!, 0)?.VersionName ?? "";
        Text(L.F("a_propos_airsoft_planner_x_tepan_games_code_sour", version, AirsoftPlanner.Core.Updates.UpdateChecker.ProjectUrl) + "\n" + L.T("licence_gpl"), 12, secondary: true)
            .AutoLinkMask = Android.Text.Util.MatchOptions.WebUrls;
        var update = PrimaryButton("", Color.Rgb(46, 125, 50));
        update.Visibility = ViewStates.Gone;
        // Seule communication engagée par l'application d'elle-même, hors échanges avec le PC de l'OP : désactivable.
        var automatic = new CheckBox(this) { Text = L.T("verifier_automatiquement_les_mises_a_jour"), Checked = Prefs.CheckUpdates, TextSize = 12 };
        automatic.CheckedChange += (_, e) =>
        {
            Prefs.CheckUpdates = e.IsChecked;
            if (e.IsChecked)
                _ = ShowUpdateAsync(update, version);
            else
                update.Visibility = ViewStates.Gone;
        };
        _target.AddView(automatic, Spaced(2));
        if (Prefs.CheckUpdates)
            _ = ShowUpdateAsync(update, version);
    }

    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private async Task ShowUpdateAsync(Button button, string installed)
    {
        // Dépôt privé ou hors ligne : rien n'est affiché.
        var release = await AirsoftPlanner.Core.Updates.UpdateChecker.GetLatestAsync(UpdateHttp);
        var current = AirsoftPlanner.Core.Updates.UpdateChecker.ParseVersion(installed);
        if (release is null || current is null || !AirsoftPlanner.Core.Updates.UpdateChecker.IsNewer(release, current))
            return;
        button.Text = L.F("nouvelle_version_x_disponible_telecharger", release.Version.ToString(3));
        button.Visibility = ViewStates.Visible;
        button.Click += (_, _) => StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(release.ApkUrl ?? release.PageUrl)));
    }

    private void Label(string text) => _target.AddView(new TextView(this) { Text = text, TextSize = 13 }, Spaced(12));

    private TextView Text(string text, float size, bool secondary = false)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        if (secondary)
            view.Alpha = 0.7f;
        view.SetTextIsSelectable(true);
        _target.AddView(view, Spaced(4));
        return view;
    }

    private EditText Input(string text, string hint, Android.Text.InputTypes type)
    {
        var input = new EditText(this) { Text = text, Hint = hint, InputType = type };
        input.SetSingleLine(true);
        _target.AddView(input, Spaced(2));
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

        _target.AddView(button, Spaced(12));
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
